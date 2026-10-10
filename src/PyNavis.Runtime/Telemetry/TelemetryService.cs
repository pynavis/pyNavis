using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace PyNavis.Runtime.Telemetry
{
    /// <summary>What one flush did, for the status line and the tests.</summary>
    public sealed class FlushResult
    {
        public static readonly FlushResult None = new FlushResult(0, 0, 0);

        public FlushResult(int sent, int spooled, int dropped)
        {
            Sent = sent;
            Spooled = spooled;
            Dropped = dropped;
        }

        /// <summary>Lines delivered, counted once per sink that took them.</summary>
        public int Sent { get; }

        /// <summary>Lines written to a sink's spool to retry later.</summary>
        public int Spooled { get; }

        /// <summary>Records lost: pushed out of a full queue, trimmed by a spool's cap,
        /// or not storable at all.</summary>
        public int Dropped { get; }

        public override string ToString() => $"{Sent} sent, {Spooled} spooled, {Dropped} dropped";
    }

    /// <summary>
    /// The firm's telemetry: tools Record what they did, and a background worker
    /// Flushes every few seconds to every sink. Nothing here runs on the UI thread
    /// except Record, which only puts the record in a bounded queue and never throws.
    ///
    /// A sink that fails gets its batch put in its own spool file (TelemetrySpool) and
    /// is retried: at the next flush after a first failure, then after 10 s, 20 s and so
    /// on up to 5 minutes, so an unreachable server is not hammered and closing
    /// Navisworks does not wait on it. The first failure is logged once; the log stays
    /// quiet about that sink until a send to it works again.
    /// </summary>
    public sealed class TelemetryService
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

        /// <summary>How long Stop waits for its final flush.</summary>
        public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(2);

        public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

        /// <summary>Lines per Send, so a long outage's backlog goes as several modest POSTs.</summary>
        public const int BatchSize = 500;

        private sealed class Target
        {
            public ITelemetrySink Sink;
            public TelemetrySpool Spool;
            public int Failures;
            public DateTime NextAttempt = DateTime.MinValue;
            public bool Logged;
        }

        private readonly TelemetryConfig _config;
        private readonly List<Target> _targets = new List<Target>();
        private readonly TelemetryQueue _queue;
        private readonly Func<DateTime> _clock;
        private readonly Action<string> _logError;

        private readonly object _flushGate = new object();
        private readonly object _statusGate = new object();
        private readonly object _lifeGate = new object();
        private Thread _worker;
        private ManualResetEvent _workerStop;

        private long _overflowSeen;
        private bool _spoolErrorLogged;
        private bool _otherErrorLogged;
        private DateTime? _lastSent;
        private string _lastError;

        /// <param name="config">Copied; later changes to it do not reach the service.</param>
        /// <param name="sinks">Where records go; none means the service records nothing.</param>
        /// <param name="spoolDir">Holds one spool file per sink, named after the sink.</param>
        /// <param name="clock">Local time, for the retry delays and the status line.</param>
        /// <param name="logError">Where the first failure of each kind is reported.</param>
        public TelemetryService(TelemetryConfig config, IEnumerable<ITelemetrySink> sinks, string spoolDir,
            Func<DateTime> clock, Action<string> logError,
            long spoolMaxBytes = TelemetrySpool.DefaultMaxBytes, int queueCapacity = TelemetryQueue.DefaultCapacity)
        {
            _config = config?.Clone() ?? new TelemetryConfig();
            _clock = clock ?? (() => DateTime.Now);
            _logError = logError ?? (_ => { });
            _queue = new TelemetryQueue(queueCapacity);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sink in sinks ?? Enumerable.Empty<ITelemetrySink>())
            {
                if (sink == null) continue;
                if (string.IsNullOrWhiteSpace(spoolDir))
                    throw new ArgumentException("Telemetry needs a folder for its spool.", nameof(spoolDir));
                var name = FolderSink.SafeName(sink.Name);
                for (var n = 2; !names.Add(name); n++) name = FolderSink.SafeName(sink.Name) + "-" + n;
                _targets.Add(new Target
                {
                    Sink = sink,
                    Spool = new TelemetrySpool(Path.Combine(spoolDir, name + ".jsonl"), spoolMaxBytes),
                });
            }
            IsActive = _config.Enabled && _targets.Count > 0;
        }

        /// <summary>The service for these settings: a FolderSink when a folder is named,
        /// an HttpSink when a URL is (one that is not http or https is logged and left
        /// out), each spooling under <paramref name="spoolDir"/>. Disabled settings make
        /// no sinks and a service that records nothing.</summary>
        public static TelemetryService Create(TelemetryConfig config, string spoolDir, Action<string> logError)
        {
            var settings = config ?? new TelemetryConfig();
            var log = logError ?? Log.Error;
            var sinks = new List<ITelemetrySink>();
            if (settings.Enabled)
            {
                if (!string.IsNullOrWhiteSpace(settings.Folder))
                    sinks.Add(new FolderSink(settings.Folder, Environment.UserName, Environment.MachineName,
                        () => DateTime.Now));
                if (!string.IsNullOrWhiteSpace(settings.Url))
                {
                    try { sinks.Add(new HttpSink(settings.Url)); }
                    catch (ArgumentException ex) { log("Telemetry: ignoring the url setting: " + ex.Message); }
                }
            }
            return new TelemetryService(settings, sinks, spoolDir, () => DateTime.Now, log);
        }

        /// <summary>Enabled, with at least one sink: Record keeps records.</summary>
        public bool IsActive { get; }

        public TelemetryConfig Config => _config.Clone();

        public IReadOnlyList<ITelemetrySink> Sinks => _targets.Select(t => t.Sink).ToList();

        /// <summary>How often the worker flushes; set before Start.</summary>
        public TimeSpan Interval { get; internal set; } = DefaultInterval;

        /// <summary>Queues a record for the next flush. Does nothing while inactive, and
        /// leaves hook runs out unless IncludeHooks is on. O(1); never throws.</summary>
        public void Record(TelemetryRecord record)
        {
            try
            {
                if (!IsActive || record == null) return;
                if (!_config.IncludeHooks && string.Equals(record.Kind, "hook", StringComparison.OrdinalIgnoreCase))
                    return;
                _queue.Enqueue(record);
            }
            catch (Exception)
            {
                // Telemetry never breaks the tool it watches.
            }
        }

        /// <summary>Sends the queued records and every spooled line to each sink not
        /// waiting out a retry delay; whatever a sink does not take goes to its spool.
        /// Never throws.</summary>
        public FlushResult Flush()
        {
            if (!IsActive) return FlushResult.None;
            lock (_flushGate)
            {
                var sent = 0;
                var spooled = 0;
                var dropped = 0;
                try
                {
                    var now = _clock();
                    var fresh = new List<string>();
                    foreach (var record in _queue.Drain(int.MaxValue))
                    {
                        try { fresh.Add(record.ToJsonLine()); }
                        catch (Exception ex)
                        {
                            dropped++;
                            ReportOnce(ref _otherErrorLogged, "Telemetry: a record could not be written and was dropped: " + ex.Message);
                        }
                    }
                    var overflow = _queue.Dropped;
                    dropped += (int)(overflow - _overflowSeen);
                    _overflowSeen = overflow;

                    string failure = null;
                    var waiting = false;
                    var delivered = false;
                    foreach (var target in _targets)
                    {
                        if (now < target.NextAttempt)
                        {
                            waiting = true;
                            Park(target, fresh, ref spooled, ref dropped);
                            continue;
                        }

                        List<string> pending;
                        try { pending = target.Spool.TakeAll(); }
                        catch (Exception ex)
                        {
                            pending = new List<string>();       // they stay in the file for next time
                            ReportOnce(ref _spoolErrorLogged, $"Telemetry: could not read {target.Spool.Path}: {ex.Message}");
                        }
                        pending.AddRange(fresh);
                        if (pending.Count == 0) continue;

                        var done = 0;
                        try
                        {
                            while (done < pending.Count)
                            {
                                var batch = pending.GetRange(done, Math.Min(BatchSize, pending.Count - done));
                                target.Sink.Send(batch);
                                done += batch.Count;
                            }
                            target.Failures = 0;
                            target.NextAttempt = DateTime.MinValue;
                            target.Logged = false;
                        }
                        catch (Exception ex)
                        {
                            target.Failures++;
                            target.NextAttempt = now + RetryDelay(target.Failures);
                            Park(target, pending.GetRange(done, pending.Count - done), ref spooled, ref dropped);
                            failure = $"The last send to {target.Sink.Target} failed: {Sentence(ex.Message)}";
                            if (!target.Logged)
                            {
                                target.Logged = true;
                                SafeLog($"Telemetry: could not send to {target.Sink.Target}: {Sentence(ex.Message)} "
                                        + "The records are kept and retried; this is not logged again until a send works.");
                            }
                        }
                        sent += done;
                        delivered |= done > 0;
                    }

                    lock (_statusGate)
                    {
                        if (delivered) _lastSent = now;
                        if (failure != null) _lastError = failure;
                        else if (!waiting) _lastError = null;
                    }
                }
                catch (Exception ex)
                {
                    ReportOnce(ref _otherErrorLogged, "Telemetry: a flush failed: " + ex.Message);
                }
                return new FlushResult(sent, spooled, dropped);
            }
        }

        /// <summary>How long a sink rests after <paramref name="failures"/> failures in a
        /// row: none after the first, then 10 s, doubling, up to MaxRetryDelay.</summary>
        public static TimeSpan RetryDelay(int failures)
        {
            if (failures <= 1) return TimeSpan.Zero;
            var seconds = DefaultInterval.TotalSeconds * Math.Pow(2, Math.Min(failures - 1, 20));
            return seconds >= MaxRetryDelay.TotalSeconds ? MaxRetryDelay : TimeSpan.FromSeconds(seconds);
        }

        /// <summary>Starts the background worker that flushes every Interval. Does nothing
        /// while inactive or already started. A dedicated background thread, not the
        /// thread pool, so a busy pool can never hold up the flush on close.</summary>
        public void Start()
        {
            if (!IsActive) return;
            lock (_lifeGate)
            {
                if (_worker != null) return;
                var stop = new ManualResetEvent(false);     // one per worker: a quick Stop and Start never cross
                _workerStop = stop;
                _worker = new Thread(() => Loop(stop)) { IsBackground = true, Name = "pyNavis telemetry" };
                _worker.Start();
            }
        }

        /// <summary>Stops the worker, which flushes what is left on its way out, and waits
        /// for that at most StopWait (records a slow sink has not taken by then are lost
        /// with the process). Without a worker the final flush gets a thread of its own.
        /// True when the final flush finished in time.</summary>
        public bool Stop()
        {
            if (!IsActive) return true;
            Thread worker;
            lock (_lifeGate)
            {
                worker = _worker;
                _worker = null;
                _workerStop?.Set();
                _workerStop = null;
            }
            try
            {
                if (worker == null)
                {
                    worker = new Thread(() => Flush()) { IsBackground = true, Name = "pyNavis telemetry final flush" };
                    worker.Start();
                }
                return worker.Join(StopWait);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>"The last send to X failed: reason." while the last attempt failed;
        /// null once a flush gets everything through.</summary>
        public string LastError
        {
            get { lock (_statusGate) return _lastError; }
        }

        /// <summary>One line for the Settings window: when records last went out, how many
        /// wait in memory and in the spool, and the last failure.</summary>
        public string Status()
        {
            if (!IsActive)
                return _config.Managed ? "Telemetry is off. Your firm manages this setting." : "Telemetry is off.";

            DateTime? lastSent;
            string lastError;
            lock (_statusGate)
            {
                lastSent = _lastSent;
                lastError = _lastError;
            }
            var waiting = _queue.Count;
            var spooled = _targets.Sum(t => t.Spool.Count());
            var text = (lastSent == null
                           ? "Nothing sent yet."
                           : "Last sent " + lastSent.Value.ToString("d MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + ".")
                       + $" {waiting} {(waiting == 1 ? "record" : "records")} waiting, {spooled} in the spool.";
            return lastError == null ? text : text + " " + lastError;
        }

        // ---- helpers ------------------------------------------------------------------

        private void Loop(WaitHandle stop)
        {
            try
            {
                while (!stop.WaitOne(Interval))
                    Flush();
                Flush();                                    // the final one, for Stop
            }
            catch (Exception ex)
            {
                ReportOnce(ref _otherErrorLogged, "Telemetry: the worker stopped: " + ex.Message);
            }
        }

        private void Park(Target target, List<string> lines, ref int spooled, ref int dropped)
        {
            if (lines.Count == 0) return;
            try
            {
                dropped += target.Spool.Append(lines);
                spooled += lines.Count;
            }
            catch (Exception ex)
            {
                dropped += lines.Count;
                ReportOnce(ref _spoolErrorLogged,
                    $"Telemetry: could not keep {lines.Count} records in {target.Spool.Path} to retry, so they were dropped: {ex.Message}");
            }
        }

        private void ReportOnce(ref bool logged, string message)
        {
            if (logged) return;
            logged = true;
            SafeLog(message);
        }

        private void SafeLog(string message)
        {
            try { _logError(message); }
            catch (Exception) { }
        }

        private static string Sentence(string message)
        {
            var text = (message ?? "").Trim();
            if (text.Length == 0) return "no reason given.";
            var last = text[text.Length - 1];
            return last == '.' || last == '!' || last == '?' ? text : text + ".";
        }
    }
}
