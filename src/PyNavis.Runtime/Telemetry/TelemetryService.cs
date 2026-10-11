using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace PyNavis.Runtime.Telemetry
{
    /// <summary>One sink that did not take the last of a flush's lines, and why.</summary>
    public sealed class FlushFailure
    {
        public FlushFailure(string target, string reason, bool refused)
        {
            Target = target ?? "";
            Reason = reason ?? "";
            Refused = refused;
        }

        /// <summary>Where the sink sends: a folder or a URL.</summary>
        public string Target { get; }

        /// <summary>Why, as a sentence that ends in a full stop: "no answer within 10 seconds."</summary>
        public string Reason { get; }

        /// <summary>The server refused the lines (TelemetryRejectedException), so they were
        /// dropped; otherwise the send failed and they are kept to retry.</summary>
        public bool Refused { get; }
    }

    /// <summary>What one flush did, for the status line, Send test record and the tests.</summary>
    public sealed class FlushResult
    {
        public static readonly FlushResult None = new FlushResult(0, 0, 0);

        public FlushResult(int sent, int spooled, int dropped, IReadOnlyList<FlushFailure> failures = null)
        {
            Sent = sent;
            Spooled = spooled;
            Dropped = dropped;
            Failures = failures ?? new FlushFailure[0];
        }

        /// <summary>Lines delivered, counted once per sink that took them.</summary>
        public int Sent { get; }

        /// <summary>Lines written to a sink's spool to retry later.</summary>
        public int Spooled { get; }

        /// <summary>Records lost: pushed out of a full queue, trimmed by a spool's cap,
        /// refused by a server, or not storable at all.</summary>
        public int Dropped { get; }

        /// <summary>The sinks that did not take the newest of this flush's lines, and why:
        /// a send that failed (what was left is kept to retry), or a last batch the server
        /// refused. Empty when every sink that was tried took the last of its lines.</summary>
        public IReadOnlyList<FlushFailure> Failures { get; }

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
    /// quiet about that sink until a send to it works again. Spooled lines leave the
    /// spool file only once a sink has taken them, so a send cut short by Navisworks
    /// closing loses nothing that was already on disk.
    ///
    /// A batch the server refuses (TelemetryRejectedException, such as a 400) is dropped,
    /// not spooled: sent again it would only be refused again and hold up every record
    /// behind it. A refusal is no reason to wait before the next send. The first one is
    /// logged with its status and how many records went; the log then stays quiet about
    /// refusals from that sink until it takes a flush's records without refusing any.
    ///
    /// Stop is the end of a service: after its final flush it disposes the sinks that
    /// hold something (HttpSink's HttpClient), and it sends nothing more. Finished is set
    /// once that is over, and a service started after it (Start with that handle) holds
    /// its worker's first flush until then, because both send the same spool files.
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
            public bool RefusalLogged;
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

        private readonly ManualResetEvent _finished = new ManualResetEvent(false);
        private volatile bool _released;
        private volatile bool _stopped;
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
        /// waiting out a retry delay; whatever a sink does not take goes to its spool,
        /// except a batch the server refuses, which is dropped. Never throws.</summary>
        public FlushResult Flush() => Flush(force: false, only: null);

        /// <summary>Flush, with every sink tried now, including one waiting out a retry
        /// delay: pynavis run's last flush, just before its Navisworks closes. Never
        /// throws.</summary>
        public FlushResult FlushNow() => Flush(force: true, only: null);

        /// <summary>
        /// Sends <paramref name="record"/> to every sink now, including one waiting out a
        /// retry delay, without the queue or the spool's backlog: Send test record answers
        /// for that one record and the destinations as they are at that moment. Queued, the
        /// record could be taken by the worker's own flush, leaving the flush that was to
        /// report on it with nothing to send. A sink that fails gets the record in its spool
        /// and a refusal drops it, as in a flush. Does nothing while inactive or once
        /// stopped; never throws.
        /// </summary>
        public FlushResult SendNow(TelemetryRecord record) =>
            record == null ? FlushResult.None : Flush(force: true, only: record);

        /// <param name="only">Null to send the queue and each spool's backlog; otherwise
        /// this record alone.</param>
        private FlushResult Flush(bool force, TelemetryRecord only)
        {
            if (!IsActive) return FlushResult.None;
            lock (_flushGate)
            {
                if (_released) return FlushResult.None;
                var sent = 0;
                var spooled = 0;
                var dropped = 0;
                var failures = new List<FlushFailure>();
                try
                {
                    var now = _clock();
                    var fresh = new List<string>();
                    foreach (var record in only != null ? new List<TelemetryRecord> { only } : _queue.Drain(int.MaxValue))
                    {
                        try { fresh.Add(record.ToJsonLine()); }
                        catch (Exception ex)
                        {
                            dropped++;
                            ReportOnce(ref _otherErrorLogged, "Telemetry: a record could not be written and was dropped: " + ex.Message);
                        }
                    }
                    if (only == null)
                    {
                        var overflow = _queue.Dropped;
                        dropped += (int)(overflow - _overflowSeen);
                        _overflowSeen = overflow;
                    }

                    string failure = null;
                    var waiting = false;
                    var delivered = false;
                    foreach (var target in _targets)
                    {
                        if (!force && now < target.NextAttempt)
                        {
                            waiting = true;
                            Park(target, fresh, ref spooled, ref dropped);
                            continue;
                        }

                        // The spool's lines stay in its file until the sink has them.
                        var backlog = new List<string>();
                        if (only == null)
                        {
                            try { backlog = target.Spool.PeekAll(); }
                            catch (Exception ex)
                            {
                                // They stay in the file for next time.
                                ReportOnce(ref _spoolErrorLogged, $"Telemetry: could not read {target.Spool.Path}: {ex.Message}");
                            }
                        }
                        var pending = new List<string>(backlog.Count + fresh.Count);
                        pending.AddRange(backlog);
                        pending.AddRange(fresh);
                        if (pending.Count == 0) continue;

                        var done = 0;                           // lines the sink took or refused
                        var unspooled = 0;                      // of those, spool lines taken out of its file
                        var took = 0;
                        var refused = 0;
                        TelemetryRejectedException refusal = null;  // set while the latest batch was refused
                        TelemetryRejectedException firstRefusal = null;
                        Exception error = null;
                        while (done < pending.Count)
                        {
                            var batch = pending.GetRange(done, Math.Min(BatchSize, pending.Count - done));
                            try
                            {
                                target.Sink.Send(batch);
                                took += batch.Count;
                                refusal = null;
                            }
                            catch (TelemetryRejectedException ex)
                            {
                                refused += batch.Count;         // dropped: sent again it would be refused again
                                refusal = ex;
                                firstRefusal = firstRefusal ?? ex;
                            }
                            catch (Exception ex)
                            {
                                error = ex;
                                break;
                            }
                            done += batch.Count;
                            unspooled = Unspool(target, backlog, unspooled, Math.Min(done, backlog.Count));
                        }
                        dropped += refused;

                        if (refused > 0)
                        {
                            if (!target.RefusalLogged)
                            {
                                target.RefusalLogged = true;
                                var why = firstRefusal.Message.Trim().TrimEnd('.');
                                SafeLog($"Telemetry: {target.Sink.Target} refused {Records(refused)} ({why}), so "
                                        + (refused == 1 ? "it was" : "they were") + " dropped rather than kept to retry. "
                                        + "This is not logged again until a send there works.");
                            }
                            if (refusal != null && error == null)
                            {
                                failures.Add(new FlushFailure(target.Sink.Target, Sentence(refusal.Message), refused: true));
                                failure = $"The last send to {target.Sink.Target} was refused, so {Records(refused)} "
                                          + (refused == 1 ? "was" : "were") + $" dropped: {Sentence(refusal.Message)}";
                            }
                        }
                        else if (took > 0)
                        {
                            target.RefusalLogged = false;
                        }

                        if (error == null)
                        {
                            // Everything reached the sink, taken or refused: no retry delay.
                            target.Failures = 0;
                            target.NextAttempt = DateTime.MinValue;
                            target.Logged = false;
                        }
                        else
                        {
                            var ex = error;
                            target.Failures++;
                            target.NextAttempt = now + RetryDelay(target.Failures);
                            // The spool's unsent lines are still in its file; only fresh ones join them.
                            var freshDone = Math.Min(fresh.Count, Math.Max(0, done - backlog.Count));
                            Park(target, fresh.GetRange(freshDone, fresh.Count - freshDone), ref spooled, ref dropped);
                            failures.Add(new FlushFailure(target.Sink.Target, Sentence(ex.Message), refused: false));
                            failure = $"The last send to {target.Sink.Target} failed: {Sentence(ex.Message)}";
                            if (!target.Logged)
                            {
                                target.Logged = true;
                                SafeLog($"Telemetry: could not send to {target.Sink.Target}: {Sentence(ex.Message)} "
                                        + "The records are kept and retried; this is not logged again until a send works.");
                            }
                        }
                        sent += took;
                        delivered |= took > 0;
                    }

                    lock (_statusGate)
                    {
                        if (delivered) _lastSent = now;
                        // A failure or refusal stays on the status line until a send gets
                        // through, not merely until a flush finds nothing to send.
                        if (failure != null) _lastError = failure;
                        else if (delivered && !waiting) _lastError = null;
                    }
                }
                catch (Exception ex)
                {
                    ReportOnce(ref _otherErrorLogged, "Telemetry: a flush failed: " + ex.Message);
                }
                return new FlushResult(sent, spooled, dropped, failures);
            }
        }

        private static string Records(int count) => count == 1 ? "1 record" : count + " records";

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
        public void Start() => Start(null);

        /// <summary>Start, with the worker's first flush held until
        /// <paramref name="after"/> is set: the Finished of the service this one replaces,
        /// whose final flush reads and empties the same spool files. A Stop meanwhile ends
        /// the wait, so app-closing still gets its final flush.</summary>
        internal void Start(WaitHandle after)
        {
            if (!IsActive || _stopped || _released) return;
            lock (_lifeGate)
            {
                if (_worker != null) return;
                var stop = new ManualResetEvent(false);     // one per worker: a quick Stop and Start never cross
                _workerStop = stop;
                _worker = new Thread(() => Loop(stop, after)) { IsBackground = true, Name = "pyNavis telemetry" };
                _worker.Start();
            }
        }

        /// <summary>Stops the worker, which flushes what is left on its way out, and waits
        /// for that at most StopWait (fresh records a slow sink has not taken by then are
        /// lost with the process; spooled ones stay in their file). Without a worker the
        /// final flush gets a thread of its own. Whichever thread flushes last then disposes
        /// the sinks, however long that flush takes, so a send is never cut off. Later
        /// calls, Start and Flush do nothing. True when the final flush finished in time.</summary>
        public bool Stop() => Stop(StopWait);

        /// <summary>Stop, waiting at most <paramref name="wait"/>. TimeSpan.Zero returns at
        /// once and leaves the final flush to its own thread; Finished says when it is over.</summary>
        internal bool Stop(TimeSpan wait)
        {
            _stopped = true;
            if (!IsActive)
            {
                ReleaseSinks();
                return true;
            }
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
                    if (_released) return true;
                    worker = new Thread(() =>
                    {
                        try { Flush(); }
                        finally { ReleaseSinks(); }
                    }) { IsBackground = true, Name = "pyNavis telemetry final flush" };
                    worker.Start();
                }
                return worker.Join(wait);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Stop has been called: the service sends nothing more once its final
        /// flush is over.</summary>
        internal bool IsStopped => _stopped;

        /// <summary>Set once a stopped service's final flush is over and its sinks are
        /// released (at once for one that is inactive). Never set before Stop.</summary>
        internal WaitHandle Finished => _finished;

        /// <summary>"The last send to X failed: reason." while the last attempt failed, or
        /// "The last send to X was refused, so N records were dropped: reason." after a
        /// refusal; null once a flush delivers and nothing failed or waits.</summary>
        public string LastError
        {
            get { lock (_statusGate) return _lastError; }
        }

        /// <summary>One line for the Settings window: when records last went out, how many
        /// wait in memory and in the spool, and the last failure. Reads no file: each spool
        /// keeps its count in memory (TelemetrySpool.Count) after a first look.</summary>
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

        private void Loop(WaitHandle stop, WaitHandle after)
        {
            try
            {
                // Never side by side with the service this one replaced: they share the spool.
                if (after != null) WaitHandle.WaitAny(new[] { stop, after });
                while (!stop.WaitOne(Interval))
                    Flush();
                Flush();                                    // the final one, for Stop
            }
            catch (Exception ex)
            {
                ReportOnce(ref _otherErrorLogged, "Telemetry: the worker stopped: " + ex.Message);
            }
            finally
            {
                ReleaseSinks();
            }
        }

        /// <summary>Disposes every sink that holds something, once, after the final flush,
        /// then sets Finished; waits for a flush under way on another thread to finish first.</summary>
        private void ReleaseSinks()
        {
            lock (_flushGate)
            {
                if (_released) return;
                _released = true;
                foreach (var target in _targets)
                {
                    try { (target.Sink as IDisposable)?.Dispose(); }
                    catch (Exception ex)
                    {
                        ReportOnce(ref _otherErrorLogged, $"Telemetry: {target.Sink.Target} did not close cleanly: {ex.Message}");
                    }
                }
                _finished.Set();
            }
        }

        /// <summary>Takes the spool's lines from <paramref name="from"/> up to
        /// <paramref name="to"/> out of its file now that the sink has them, and returns how
        /// far that got. When the file cannot be written they stay, the next batch's call
        /// tries again, and at worst they are sent twice later; they are never lost.</summary>
        private int Unspool(Target target, List<string> backlog, int from, int to)
        {
            if (to <= from) return from;
            try
            {
                target.Spool.Discard(backlog.GetRange(from, to - from));
                return to;
            }
            catch (Exception ex)
            {
                ReportOnce(ref _spoolErrorLogged,
                    $"Telemetry: could not take sent records out of {target.Spool.Path}, so they may be sent again: {ex.Message}");
                return from;
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
