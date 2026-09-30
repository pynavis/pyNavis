using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Threading;
using PyNavis.Runtime.Config;

namespace PyNavis.Runtime.Update
{
    /// <summary>
    /// The networked half of updates. Once a day at startup, in the background, it asks
    /// GitHub for the latest release (UpdateCheck reads the answer). When a newer one is
    /// out and the user lets it (updates.installOnClose, on by default), it downloads
    /// the setup exe into %APPDATA%\pyNavis\updates, keeps it only when its SHA-256
    /// matches the release's, and when Navisworks closes starts it quietly with
    /// /WAITPID so it installs once Navisworks has exited (the installer cannot replace
    /// a DLL Navisworks holds). Otherwise it says the release is available. Offline, a
    /// rate limit or any other failure is one log line and a status for Settings; it
    /// never interrupts anyone.
    /// </summary>
    public static class UpdateService
    {
        private static readonly object Gate = new object();
        private static string _appData;
        private static Dispatcher _ui;
        private static bool _checking;
        private static bool _wired;

        /// <summary>The last failure, for Settings; null when the last check worked.</summary>
        public static string LastError { get; private set; }

        public sealed class State
        {
            public DateTime? LastCheckUtc;
            public string Latest;
            public string Page;
            public string Installer;
            public string InstallerVersion;
            public string InstallerSha256;
        }

        private static string AppData => _appData ?? Path.GetDirectoryName(RuntimeHost.UserConfigPath);
        public static string UpdatesDir => Path.Combine(AppData, "updates");
        private static string StatePath => Path.Combine(UpdatesDir, "state.json");

        /// <summary>At startup: forget an installer that has done its job, listen for
        /// Navisworks closing, and check when a day has passed.</summary>
        public static void Start(PyNavisConfig config, string appData)
        {
            _appData = appData;
            _ui = Dispatcher.CurrentDispatcher;
            var state = ReadState();
            if (state.Installer != null && UpdateCheck.ShouldDiscard(PyNavisVersion.Product, state.InstallerVersion))
            {
                TryDelete(state.Installer);
                state.Installer = state.InstallerVersion = state.InstallerSha256 = null;
                WriteState(state);
            }
            if (!_wired)
            {
                _wired = true;
                Events.NavisEvents.Raised += e =>
                {
                    if (e.Event == Events.NavisEvent.AppClosing) InstallOnClose();
                };
            }
            if (config.UpdatesCheck && UpdateCheck.Due(DateTime.UtcNow, state.LastCheckUtc))
                Task.Run(() => Check(config, manual: false));
        }

        /// <summary>Settings' "Check now": checks whatever the schedule says, and answers
        /// with the status line instead of a toast.</summary>
        public static Task<string> CheckNowAsync() => Task.Run(() =>
        {
            var config = PyNavisConfig.Load(RuntimeHost.UserConfigPath);
            Check(config, manual: true);
            return Status(config.UpdatesSkip);
        });

        /// <summary>The line Settings shows, given the version the user skipped (if any).</summary>
        public static string Status(string skipped)
        {
            var state = ReadState();
            var line = UpdateCheck.StatusLine(PyNavisVersion.Product, state.Latest,
                state.Installer != null && File.Exists(state.Installer) ? state.InstallerVersion : null,
                skipped, state.LastCheckUtc?.ToLocalTime());
            return LastError == null ? line : line + " The last check failed: " + LastError;
        }

        /// <summary>The newest release the last check saw, for Skip this version; null when
        /// nothing newer than the running version is known.</summary>
        public static string NewerVersion()
        {
            var latest = ReadState().Latest;
            return UpdateCheck.CompareVersions(latest, PyNavisVersion.Product) > 0 ? latest : null;
        }

        public static string ReleasePage() =>
            ReadState().Page ?? "https://github.com/pynavis/pyNavis/releases/latest";

        private static void Check(PyNavisConfig config, bool manual)
        {
            lock (Gate)
            {
                if (_checking) return;
                _checking = true;
            }
            try
            {
                var release = UpdateCheck.ParseRelease(Fetch());
                if (release == null) throw new InvalidDataException("GitHub did not answer with a release");
                var state = ReadState();
                state.LastCheckUtc = DateTime.UtcNow;
                state.Latest = release.Version;
                state.Page = release.Page;
                WriteState(state);
                LastError = null;
                Log.Info($"Update check: latest release {release.Version}, running {PyNavisVersion.Product}.");

                if (!UpdateCheck.IsNewer(PyNavisVersion.Product, release, config.UpdatesSkip)) return;
                if (config.UpdatesInstallOnClose && release.InstallerUrl != null && release.Sha256 != null)
                {
                    if (state.InstallerVersion != release.Version || !File.Exists(state.Installer ?? ""))
                    {
                        state.Installer = Download(release);
                        state.InstallerVersion = release.Version;
                        state.InstallerSha256 = release.Sha256;
                        WriteState(state);
                    }
                    if (!manual) Tell(UpdateCheck.ReadyMessage(release));
                }
                else if (!manual)
                {
                    Tell(UpdateCheck.AvailableMessage(release));
                }
            }
            catch (Exception ex)
            {
                LastError = ex is HttpRequestException || ex is TaskCanceledException || ex is WebException
                    ? "GitHub could not be reached." : ex.Message;
                Log.Error("Update check failed", ex);
            }
            finally
            {
                lock (Gate) _checking = false;
            }
        }

        /// <summary>When Navisworks closes with a verified, newer installer downloaded:
        /// start it detached, waiting on this process to exit.</summary>
        private static void InstallOnClose()
        {
            try
            {
                var config = PyNavisConfig.Load(RuntimeHost.UserConfigPath);
                if (!config.UpdatesInstallOnClose) return;
                var state = ReadState();
                if (state.Installer == null || !File.Exists(state.Installer)) return;
                if (UpdateCheck.ShouldDiscard(PyNavisVersion.Product, state.InstallerVersion)) return;
                if (UpdateCheck.CompareVersions(state.InstallerVersion, config.UpdatesSkip) == 0) return;
                if (!string.Equals(Sha256(state.Installer), state.InstallerSha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Error($"Update: {state.Installer} no longer matches its SHA-256 - not run, deleted.");
                    TryDelete(state.Installer);
                    return;
                }

                var log = Path.Combine(UpdatesDir, $"install-{state.InstallerVersion}.log");
                var arguments = UpdateCheck.InstallerArguments(Process.GetCurrentProcess().Id, log);
                Process.Start(new ProcessStartInfo(state.Installer, arguments) { UseShellExecute = true });
                Log.Info($"Update: pyNavis {state.InstallerVersion} will install once Navisworks has closed ({state.Installer} {arguments}).");
            }
            catch (Exception ex)
            {
                Log.Error("Update: could not start the installer on close", ex);
            }
        }

        // ---- network ------------------------------------------------------------------

        private static HttpClient Client(TimeSpan timeout)
        {
            // Navisworks may leave the process on older TLS defaults; GitHub needs 1.2.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                UseDefaultCredentials = true,          // the corporate proxy, as the user
                AllowAutoRedirect = true,              // downloads redirect to GitHub's CDN
            };
            var client = new HttpClient(handler) { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("pyNavis/" + PyNavisVersion.Product);
            return client;
        }

        private static string Fetch()
        {
            using (var client = Client(TimeSpan.FromSeconds(15)))
            {
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                return client.GetStringAsync(UpdateCheck.LatestReleaseApi).GetAwaiter().GetResult();
            }
        }

        private static string Download(Release release)
        {
            Directory.CreateDirectory(UpdatesDir);
            var target = Path.Combine(UpdatesDir, release.InstallerName);
            var part = target + ".part";
            using (var client = Client(TimeSpan.FromMinutes(10)))
            using (var response = client.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead)
                       .GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var output = File.Create(part))
                    input.CopyTo(output);
            }
            var actual = Sha256(part);
            if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(part);
                throw new InvalidDataException($"the download's SHA-256 ({actual}) is not the release's ({release.Sha256})");
            }
            TryDelete(target);
            File.Move(part, target);
            Log.Info($"Update: downloaded and verified {target}.");
            return target;
        }

        // ---- state --------------------------------------------------------------------

        public static State ReadState()
        {
            var state = new State();
            try
            {
                if (!File.Exists(StatePath)) return state;
                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath));
                if (data == null) return state;
                if (data.TryGetValue("lastCheck", out var last) && last is string lastText
                    && DateTime.TryParse(lastText, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
                    state.LastCheckUtc = when;
                state.Latest = Text(data, "latest");
                state.Page = Text(data, "page");
                state.Installer = Text(data, "installer");
                state.InstallerVersion = Text(data, "installerVersion");
                state.InstallerSha256 = Text(data, "installerSha256");
            }
            catch (Exception ex)
            {
                Log.Error("Update: could not read " + StatePath, ex);
            }
            return state;
        }

        private static void WriteState(State state)
        {
            try
            {
                Directory.CreateDirectory(UpdatesDir);
                var data = new Dictionary<string, object>
                {
                    ["lastCheck"] = state.LastCheckUtc?.ToString("o", CultureInfo.InvariantCulture),
                    ["latest"] = state.Latest,
                    ["page"] = state.Page,
                    ["installer"] = state.Installer,
                    ["installerVersion"] = state.InstallerVersion,
                    ["installerSha256"] = state.InstallerSha256,
                };
                File.WriteAllText(StatePath, new JavaScriptSerializer().Serialize(data), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Error("Update: could not write " + StatePath, ex);
            }
        }

        private static void Tell((string title, string detail) message)
        {
            var ui = _ui;
            if (ui == null) return;
            ui.BeginInvoke(new Action(() => Forms.Toast.Show("info", message.title, message.detail)));
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }

        private static void TryDelete(string path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Log.Error("Update: could not delete " + path, ex); }
        }

        private static string Text(Dictionary<string, object> data, string key) =>
            data.TryGetValue(key, out var value) ? value as string : null;
    }
}
