using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Update;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The networked half of updates, against a stand-in for GitHub and a temp folder
    /// in place of %APPDATA%\pyNavis. UpdateService is static, so every test here puts
    /// its seams back when it is done.
    /// </summary>
    public class UpdateServiceTests : IDisposable
    {
        private readonly string _appData = Path.Combine(
            Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
        private readonly Func<HttpMessageHandler> _handler = UpdateService.HandlerFactory;
        private readonly Func<PyNavisConfig> _config = UpdateService.LoadConfig;
        private readonly GitHubStub _github = new GitHubStub();

        public UpdateServiceTests()
        {
            Directory.CreateDirectory(_appData);
            UpdateService.UseAppData(_appData);
            UpdateService.HandlerFactory = () => _github;
            UpdateService.LoadConfig = () => new PyNavisConfig();
        }

        public void Dispose()
        {
            _github.Open();
            UpdateService.HandlerFactory = _handler;
            UpdateService.LoadConfig = _config;
            UpdateService.UseAppData(null);
            try { Directory.Delete(_appData, true); } catch { }
        }

        private string Updates => Path.Combine(_appData, "updates");

        /// <summary>Answers the latest-release request with <see cref="ReleaseJson"/> (held
        /// until <see cref="Open"/> after <see cref="Hold"/>) and any other request with
        /// the installer.</summary>
        private sealed class GitHubStub : HttpMessageHandler
        {
            public readonly TaskCompletionSource<bool> Asked = new TaskCompletionSource<bool>();
            public string ReleaseJson;
            public byte[] Installer = new byte[0];
            public int ReleaseRequests;
            private TaskCompletionSource<bool> _held;

            public void Hold() => _held = new TaskCompletionSource<bool>();

            public void Open() => _held?.TrySetResult(true);

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri.AbsoluteUri == UpdateCheck.LatestReleaseApi)
                {
                    Interlocked.Increment(ref ReleaseRequests);
                    Asked.TrySetResult(true);
                    if (_held != null) await _held.Task.ConfigureAwait(false);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(ReleaseJson, Encoding.UTF8, "application/json"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) };
            }
        }

        private static string Release(string version, byte[] installer = null)
        {
            var release = new Dictionary<string, object>
            {
                ["tag_name"] = "v" + version,
                ["html_url"] = "https://example.invalid/releases/v" + version,
            };
            if (installer != null)
            {
                string sha;
                using (var hash = SHA256.Create())
                    sha = string.Concat(hash.ComputeHash(installer).Select(b => b.ToString("x2")));
                var name = $"pyNavis-{version}-setup.exe";
                release["assets"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = name,
                        ["browser_download_url"] = "https://example.invalid/download/" + name,
                        ["size"] = installer.Length,
                        ["digest"] = "sha256:" + sha,
                    },
                };
            }
            return new JavaScriptSerializer().Serialize(release);
        }

        // GitHub never asks for Windows credentials. Offering them to every server
        // (UseDefaultCredentials) sent them to the origin, not to the corporate proxy
        // the old comment meant: only the proxy may have them.
        [Fact]
        public void TheClient_OffersWindowsCredentialsToTheProxy_NotToGitHub()
        {
            using (var handler = UpdateService.CreateHandler())
            {
                Assert.False(handler.UseDefaultCredentials);
                Assert.Same(CredentialCache.DefaultCredentials, handler.DefaultProxyCredentials);
                Assert.True(handler.AllowAutoRedirect);
            }
        }

        // A newer release replaces an installer that was downloaded but never ran: the
        // state moves to the new file, and the older exe must not stay in the folder
        // for ever.
        [Fact]
        public async Task ANewerInstaller_DeletesTheOneItSupersedes()
        {
            Directory.CreateDirectory(Updates);
            var old = Path.Combine(Updates, "pyNavis-99.0.0-setup.exe");
            File.WriteAllBytes(old, new byte[] { 1, 2, 3 });
            var halfDone = Path.Combine(Updates, "pyNavis-98.0.0-setup.exe.part");
            File.WriteAllBytes(halfDone, new byte[] { 4 });
            var log = Path.Combine(Updates, "install-99.0.0.log");
            File.WriteAllText(log, "kept");
            File.WriteAllText(Path.Combine(Updates, "state.json"), new JavaScriptSerializer().Serialize(
                new Dictionary<string, object>
                {
                    ["latest"] = "99.0.0",
                    ["installer"] = old,
                    ["installerVersion"] = "99.0.0",
                    ["installerSha256"] = "00",
                }));
            var bytes = Encoding.ASCII.GetBytes("new installer");
            _github.Installer = bytes;
            _github.ReleaseJson = Release("99.0.1", bytes);

            await UpdateService.CheckNowAsync();

            var fresh = Path.Combine(Updates, "pyNavis-99.0.1-setup.exe");
            Assert.Null(UpdateService.LastError);
            Assert.Equal(fresh, UpdateService.ReadState().Installer);
            Assert.True(File.Exists(fresh));
            Assert.False(File.Exists(old), "the superseded installer is still there");
            Assert.False(File.Exists(halfDone), "an older half-done download is still there");
            Assert.True(File.Exists(log));
            Assert.True(File.Exists(Path.Combine(Updates, "state.json")));
        }

        // Settings > Check now while the startup check is still talking to GitHub used to
        // answer at once with the status from before either check: stale text, no error.
        // It must wait for that check and answer with what it found, without asking twice.
        [Fact]
        public async Task CheckNow_DuringTheStartupCheck_WaitsForIt_AndAnswersWithItsResult()
        {
            _github.ReleaseJson = Release("99.0.1");
            _github.Hold();
            var startup = UpdateService.CheckInBackground(new PyNavisConfig());
            Assert.Same(_github.Asked.Task, await Task.WhenAny(_github.Asked.Task, Task.Delay(10000)));

            var now = UpdateService.CheckNowAsync();
            var early = await Task.WhenAny(now, Task.Delay(300));
            _github.Open();
            var status = await now;
            await startup;

            Assert.NotSame(now, early);
            Assert.Equal("pyNavis 99.0.1 is available.", status);
            Assert.Equal(1, _github.ReleaseRequests);
        }
    }
}
