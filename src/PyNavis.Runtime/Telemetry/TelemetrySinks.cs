using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PyNavis.Runtime.Telemetry
{
    /// <summary>
    /// Somewhere records go. Send delivers every line (each one JSON object, no line
    /// end) or throws; the service decides what a failure means (it spools and retries).
    /// Called only from the telemetry worker, never on the UI thread.
    /// </summary>
    public interface ITelemetrySink
    {
        /// <summary>"folder" or "http": names this sink's spool file.</summary>
        string Name { get; }

        /// <summary>Where it sends, for the log and the status line.</summary>
        string Target { get; }

        void Send(IReadOnlyList<string> lines);
    }

    /// <summary>
    /// Appends to &lt;folder&gt;\&lt;machine&gt;-&lt;user&gt;-&lt;yyyy-MM-dd&gt;.jsonl: one
    /// file per machine, user and day, so nobody writes into anyone else's file and a
    /// firm can collect a day by its name. UTF-8 without a BOM, "\n" line ends, opened
    /// for append with read and write sharing so a collector reading the file never
    /// blocks it. Each send is one write.
    /// </summary>
    public sealed class FolderSink : ITelemetrySink
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private readonly string _folder;
        private readonly string _prefix;
        private readonly Func<DateTime> _clock;

        public FolderSink(string folder, string user, string machine, Func<DateTime> clock)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("A folder sink needs a folder.", nameof(folder));
            _folder = folder.Trim();
            _prefix = SafeName(machine) + "-" + SafeName(user) + "-";
            _clock = clock ?? (() => DateTime.Now);
        }

        public string Name => "folder";
        public string Target => _folder;

        /// <summary>Today's file (by the clock this sink was given).</summary>
        public string CurrentFile =>
            Path.Combine(_folder, _prefix + _clock().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");

        public void Send(IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0) return;
            var text = new StringBuilder();
            foreach (var line in lines) text.Append(line).Append('\n');
            var bytes = Utf8.GetBytes(text.ToString());

            Directory.CreateDirectory(_folder);
            using (var stream = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>A user or machine name as a file name part: characters Windows refuses
        /// become "_", and an empty name is "unknown".</summary>
        internal static string SafeName(string name)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0) return "unknown";
            var invalid = Path.GetInvalidFileNameChars();
            return new string(trimmed.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }

    /// <summary>
    /// POSTs records to the firm's endpoint as one JSON array per batch
    /// (application/json, User-Agent pyNavis/&lt;version&gt;). Any answer but a 2xx is a
    /// failure. The production handler goes through the corporate proxy as the user,
    /// like the update check.
    /// </summary>
    public sealed class HttpSink : ITelemetrySink, IDisposable
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        private readonly Uri _url;
        private readonly HttpClient _client;
        private readonly TimeSpan _timeout;

        /// <summary>The production sink: TLS 1.2, the user's proxy credentials, redirects followed.</summary>
        public HttpSink(string url) : this(url, null, DefaultTimeout) { }

        /// <summary>A null handler means the production one; tests pass a fake.</summary>
        public HttpSink(string url, HttpMessageHandler handler, TimeSpan timeout)
        {
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException($"'{url}' is not an http or https address.", nameof(url));
            _url = parsed;
            _timeout = timeout > TimeSpan.Zero ? timeout : DefaultTimeout;
            _client = handler == null
                ? new HttpClient(ProductionHandler(), disposeHandler: true)
                : new HttpClient(handler, disposeHandler: false);
            _client.Timeout = _timeout;
            _client.DefaultRequestHeaders.UserAgent.TryParseAdd("pyNavis/" + PyNavisVersion.Product);
        }

        public string Name => "http";
        public string Target => _url.ToString();

        public void Send(IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0) return;
            var body = "[" + string.Join(",", lines) + "]";
            try
            {
                using (var content = new StringContent(body, new UTF8Encoding(false), "application/json"))
                using (var response = _client.PostAsync(_url, content).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException(
                            $"the server answered {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd());
                }
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException($"no answer within {_timeout.TotalSeconds:0} seconds", ex);
            }
            catch (HttpRequestException ex) when (ex.InnerException != null)
            {
                // "An error occurred while sending the request" says nothing; the inner one
                // names the proxy, the DNS name or the certificate.
                throw new HttpRequestException(ex.InnerException.Message, ex);
            }
        }

        public void Dispose() => _client.Dispose();

        private static HttpMessageHandler ProductionHandler()
        {
            // Navisworks may leave the process on older TLS defaults.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            return new HttpClientHandler
            {
                UseDefaultCredentials = true,          // the corporate proxy, as the user
                AllowAutoRedirect = true,
            };
        }
    }
}
