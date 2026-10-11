using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Install;
using PyNavis.Runtime.Engine;
using PyNavis.Runtime.Ribbon;

namespace PyNavis.Runtime
{
    /// <summary>
    /// Runtime entry points, called (via reflection-safe seams) from the loader.
    /// </summary>
    public static class RuntimeHost
    {
        private static readonly IRibbonProvider RibbonProvider = new AdWindowsRibbonProvider();
        private static string ApiVersion => PyNavisVersion.HostApiVersion;

        /// <summary>Host release year (2023-2027), for min/max_host_version gating.</summary>
        public static int HostYear => PyNavisVersion.HostYear;

        private static bool _booted;
        private static bool _startupScriptsRan;
        private static List<ExtensionModel> _extensions = new List<ExtensionModel>();
        private static List<string> _libraries = new List<string>();

        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyNavis", "config.json");

        /// <summary>The parsed extensions behind the current ribbon (Shortcuts editor reads these).</summary>
        public static IReadOnlyList<ExtensionModel> Extensions => _extensions;

        /// <summary>The user config.json path, for features that write settings back.</summary>
        public static string UserConfigPath => ConfigPath;

        /// <summary>Called once at Navisworks startup from BootPlugin (EventWatcherPlugin).</summary>
        public static void Boot()
        {
            if (_booted) return;
            _booted = true;

            Log.Info("==== pyNavis runtime booting ====");
            Log.Info(PyNavisVersion.Summary());
            Log.Info($"Navisworks API assembly: {ApiVersion}");
            var automated = Execution.Runner.AutomatedSession.Detect();

            var userConfig = PyNavisConfig.Load(ConfigPath);
            var runtimeDir = Path.GetDirectoryName(typeof(RuntimeHost).Assembly.Location);
            _libraries = LoadLibraries(userConfig);
            EngineManager.Configure(EngineConfigBuilder.Build(runtimeDir, userConfig, _libraries));
            _extensions = LoadExtensions(userConfig);

            // Here rather than with the interactive stages, which a pynavis run session
            // skips: runs from the command line are recorded too.
            StartTelemetry(userConfig);

            if (automated && userConfig.AutomatedBootFull)
            {
                Log.Info("automatedBoot is \"full\" in config.json: booting the interactive stages in this automated session.");
                automated = false;
            }

            // A hidden pynavis run session builds none of it: no one is there to use a
            // ribbon or read a notice, and startup.py is the runner's to start.
            Execution.Runner.AutomatedSession.QueueInteractiveStages(automated, RibbonBootstrap.WhenRibbonReady, () =>
            {
                var steps = new StepRunner();
                RunStartupStages(steps, userConfig);
                steps.Run("View overlay", () => Overlay.OverlayRegistry.EnsureLoaded());
                steps.Run("App-init event", () => Events.NavisEvents.RaiseWhenIdle(Events.NavisEvent.AppInit));
                // What the last update kept of the user's work, and a warning when there
                // is work in pyNavis.extension, which the next update replaces.
                steps.Run("Upgrade notices", () =>
                {
                    var appData = Path.GetDirectoryName(ConfigPath);
                    Upgrade.UpgradeNotices.ShowLastUpgradeReport(appData);
                    Upgrade.UpgradeNotices.WarnAboutWorkInShippedExtension(_extensions, appData);
                });
                // Once a day, in the background: is a newer release out? (Settings > Updates)
                steps.Run("Update check", () =>
                    Update.UpdateService.Start(userConfig, Path.GetDirectoryName(ConfigPath)));
                ReportFailures(steps, "pyNavis started with problems");
            });

            Log.Info("Boot sequence complete (ribbon build may be deferred until ribbon exists).");
        }

        /// <summary>
        /// Runs every extension's startup.py unless Boot, Reload or an earlier call already
        /// has since the last Reload. Boot queues startup scripts behind the ribbon, and in
        /// the hidden Navisworks that pynavis run drives the ribbon may never be ready, so the
        /// runner calls this before each script.
        /// </summary>
        public static void EnsureStartupScriptsRan()
        {
            if (_startupScriptsRan) return;
            Execution.StartupRunner.RunAll(_extensions);
            _startupScriptsRan = true;
        }

        /// <summary>Rescans extension folders and rebuilds the ribbon. No restart needed.</summary>
        public static void Reload()
        {
            Log.Info("==== pyNavis reload ====");
            try
            {
                RibbonProvider.Teardown();
                Input.ShortcutManager.UninstallHook(); // no hook may outlive its map
                Forms.Toast.CloseAll(); // a topmost window must never outlive the runtime that made it
                Forms.Banner.CloseAll();
                Output.PyNavisTheme.Invalidate(); // theme may have changed since boot
                Forms.FluentChrome.InvalidateAccent(); // and so may the Windows accent
                var userConfig = PyNavisConfig.Load(ConfigPath);
                _extensions = LoadExtensions(userConfig);
                // Keeps the running service when the settings in force have not changed
                // (Settings' Save has usually just applied them); otherwise starts one for
                // the settings now in config.json and the firm file, and the old one
                // flushes on its way out without holding up the Reload.
                Telemetry.TelemetryHost.Configure(userConfig.Telemetry);

                var libsNow = LoadLibraries(userConfig);
                if (!libsNow.OrderBy(l => l).SequenceEqual(_libraries.OrderBy(l => l), StringComparer.OrdinalIgnoreCase))
                    Forms.Toast.Show("info", "Library extensions changed",
                        "Added or removed *.lib folders apply after a Navisworks restart.");
                if (_libraries.Count > 0) EngineManager.InvalidateModules(_libraries);

                // Idempotent no-ops when Boot already wired them; they are what makes
                // Reload able to heal a Boot stage that failed.
                var steps = new StepRunner();
                _startupScriptsRan = false;   // every Reload re-runs startup.py
                RunStartupStages(steps, userConfig);
                steps.Run("App-init event", () => Events.NavisEvents.RaiseWhenIdle(Events.NavisEvent.AppInit));

                // Bundle-path modules invalidate per run; pynavislib is a base search
                // path and stays cached, so Reload is where library edits get picked up.
                steps.Run("Library refresh", () =>
                {
                    var runtimeDir = Path.GetDirectoryName(typeof(RuntimeHost).Assembly.Location);
                    var pynavislib = EngineConfigBuilder.ResolvePyNavisLib(runtimeDir, userConfig);
                    if (pynavislib != null)
                        EngineManager.InvalidateModules(new[] { pynavislib });
                });
                ReportFailures(steps, "Reload finished with problems");
            }
            catch (Exception ex)
            {
                Log.Error("Reload failed", ex);
                TryToast("Reload failed", "Details are in " + Log.LogDir);
            }
        }

        /// <summary>
        /// The stages Boot and Reload share, each failing alone: one bundle that throws
        /// while the ribbon builds must not also cost the user their hooks, context
        /// greying and keyboard shortcuts.
        /// </summary>
        private static void RunStartupStages(StepRunner steps, PyNavisConfig userConfig)
        {
            steps.Run("Event wiring", Events.NavisEvents.Wire);
            // Configure before Build so button tooltips can show resolved chords.
            steps.Run("Shortcuts", () => Input.ShortcutManager.Configure(_extensions, userConfig));
            // Through EnsureStartupScriptsRan so Boot's deferred step is a no-op when
            // pynavis run already ran them; Reload clears the flag to run them again.
            steps.Run("Startup scripts", EnsureStartupScriptsRan);
            steps.Run("Dock panels", () => Panes.PaneRegistry.Configure(_extensions, userConfig, ConfigPath));
            // Before Build, like Shortcuts above: the markers are read while captions and
            // icons are being made, so a config.json edit lands on the next Reload.
            steps.Run("Ribbon markers", () => Ribbon.RibbonMarkers.Configure(userConfig));
            steps.Run("Ribbon", () => RibbonProvider.Build(_extensions));
            steps.Run("Hooks", () => Events.HookRunner.Configure(_extensions));
            steps.Run("Context rules", () =>
            {
                Ribbon.ContextGate.Wire();
                Ribbon.ContextGate.Refresh();
            });
            steps.Run("Keyboard shortcuts", Input.ShortcutManager.InstallHook);
        }

        private static bool _telemetryClosingWired;

        /// <summary>The telemetry service for this session (Settings > Telemetry), stopped
        /// on app-closing so its worker flushes what is left. Never throws.</summary>
        private static void StartTelemetry(PyNavisConfig userConfig)
        {
            try
            {
                Telemetry.TelemetryHost.Extensions = () => _extensions;
                Telemetry.TelemetryHost.Configure(userConfig.Telemetry);
                if (_telemetryClosingWired) return;
                _telemetryClosingWired = true;
                Events.NavisEvents.Raised += e =>
                {
                    if (e.Event == Events.NavisEvent.AppClosing) Telemetry.TelemetryHost.Stop();
                };
            }
            catch (Exception ex)
            {
                Log.Error("Telemetry could not start", ex);
            }
        }

        /// <summary>A failed stage used to be a log line only, so the user saw a missing
        /// ribbon or dead shortcuts and nothing else. Say it once, with the log folder.</summary>
        private static void ReportFailures(StepRunner steps, string headline)
        {
            var summary = steps.Summary();
            if (summary != null) TryToast(headline, summary);

            // "My button is not there" with nothing on screen to say why is the hardest
            // question a bundle author can be left with. The parser's own words answer it.
            var skipped = _extensions.SelectMany(e => e.Problems).ToList();
            if (skipped.Count == 0) return;
            try
            {
                Forms.Toast.Show("warning", ExtensionModel.ProblemsHeadline(skipped.Count),
                    skipped[0] + (skipped.Count > 1 ? " The rest are in " + Log.LogDir : ""));
            }
            catch (Exception ex)
            {
                Log.Error("Could not show the skipped-bundles toast", ex);
            }
        }

        private static void TryToast(string message, string detail)
        {
            try
            {
                Forms.Toast.Show("error", message, detail);
            }
            catch (Exception ex)
            {
                Log.Error("Could not show the failure toast", ex);
            }
        }

        /// <summary>Engine + environment diagnostic reachable through PyNavis.Console
        /// (plugin id, *.linkbutton, or ExecuteAddInPlugin), never through a ribbon
        /// button of its own.</summary>
        public static void RunConsoleFallback()
        {
            Log.Info("Console fallback invoked.");

            const string code = @"
import sys
import clr
clr.AddReference('System.Windows.Forms')
from System.Windows.Forms import MessageBox
MessageBox.Show(
    'pyNavis diagnostic\n\nIronPython %s\nNavisworks API %s\nExtensions loaded: %s'
        % (sys.version, __api_version__, __extension_count__),
    'pyNavis')
";
            var request = new ScriptRequest { Code = code };
            request.Globals["__api_version__"] = ApiVersion;
            request.Globals["__extension_count__"] = _extensions.Count.ToString();

            var result = EngineManager.GetEngine().Execute(request);
            if (!result.Succeeded)
            {
                Log.Error("Diagnostic script failed:\n" + result.ErrorText);
                MessageBox.Show("pyNavis script error:\n\n" + result.ErrorText, "pyNavis",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static List<string> ExtensionRoots(PyNavisConfig userConfig)
        {
            return InstallPaths.ExtensionRoots(
                userConfig.ExtensionPaths, InstallPaths.DefaultExtensionRoots(),
                root => InstallPaths.UsableDefaultRoot(root, InstallPaths.MachineExtensionRoot,
                    Directory.Exists, AdminOnlyOrWarn));
        }

        private static bool AdminOnlyOrWarn(string root)
        {
            if (MachineFolderTrust.IsAdminOnly(root)) return true;
            Log.Error($"Extension root '{root}' skipped: users other than administrators can write to it, "
                     + "so code in it could come from anyone on this PC. Restrict it to administrators "
                     + "(read-only for Users), or list it under extensions in config.json to load it anyway.");
            return false;
        }

        private static List<ExtensionModel> LoadExtensions(PyNavisConfig userConfig)
        {
            var extensions = new List<ExtensionModel>();
            // Per root, so one that is offline or unreadable never costs the others.
            new StepRunner().Each(ExtensionRoots(userConfig), root => "Extension root '" + root + "'", root =>
            {
                var found = BundleParser.ParseRoot(root);
                Log.Info($"Extension root '{root}': {found.Count} extension(s).");
                extensions.AddRange(found);
            });
            return extensions;
        }

        private static List<string> LoadLibraries(PyNavisConfig userConfig)
        {
            var libs = new List<string>();
            new StepRunner().Each(ExtensionRoots(userConfig), root => "Library root '" + root + "'", root =>
            {
                var found = BundleParser.FindLibraries(root);
                if (found.Count > 0)
                    Log.Info($"Extension root '{root}': {found.Count} library extension(s).");
                libs.AddRange(found);
            });
            return libs;
        }

    }
}
