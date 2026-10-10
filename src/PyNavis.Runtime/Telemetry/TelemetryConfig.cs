using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace PyNavis.Runtime.Telemetry
{
    /// <summary>
    /// Whether pyNavis records what its tools do, and where the records go: a shared
    /// folder, an HTTP endpoint, or both. Off by default, and nothing leaves the machine
    /// until someone names a place for it.
    ///
    /// Two sources, never mixed: the user's "telemetry" section in config.json, and a
    /// firm file at %PROGRAMDATA%\pyNavis\telemetry.json with the same keys. When the
    /// firm file exists and parses it decides everything (Managed is then true and the
    /// Settings window should show the values read-only); a key it leaves out takes the
    /// default, not the user's value.
    /// </summary>
    public sealed class TelemetryConfig
    {
        /// <summary>"enabled". Default false.</summary>
        public bool Enabled { get; set; }

        /// <summary>"folder": where FolderSink writes; "" for no folder.</summary>
        public string Folder { get; set; } = "";

        /// <summary>"url": where HttpSink posts; "" for no endpoint.</summary>
        public string Url { get; set; } = "";

        /// <summary>"includeHooks": record hook runs too. Default false: hooks fire on
        /// every selection change and would drown the clicks.</summary>
        public bool IncludeHooks { get; set; }

        /// <summary>True when these values came from the firm file. Never written to config.json.</summary>
        public bool Managed { get; set; }

        /// <summary>A folder or a URL is set.</summary>
        public bool HasTarget => !string.IsNullOrWhiteSpace(Folder) || !string.IsNullOrWhiteSpace(Url);

        /// <summary>On, with somewhere to send to.</summary>
        public bool IsActive => Enabled && HasTarget;

        /// <summary>%PROGRAMDATA%\pyNavis\telemetry.json, the file a firm deploys.</summary>
        public static string DefaultFirmPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "pyNavis", "telemetry.json");

        public TelemetryConfig Clone() => new TelemetryConfig
        {
            Enabled = Enabled, Folder = Folder, Url = Url, IncludeHooks = IncludeHooks, Managed = Managed,
        };

        /// <summary>
        /// The settings in force: the firm file's when it exists and parses (Managed),
        /// otherwise a copy of the user's. A firm file that cannot be read or parsed is
        /// logged and ignored, so a typo on one machine does not switch anything on or off.
        /// </summary>
        public static TelemetryConfig Resolve(TelemetryConfig user, string firmFilePath, Action<string> logError = null)
        {
            var fallback = user?.Clone() ?? new TelemetryConfig();
            fallback.Managed = false;
            if (string.IsNullOrEmpty(firmFilePath) || !File.Exists(firmFilePath)) return fallback;

            try
            {
                var text = File.ReadAllText(firmFilePath);
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidDataException("the file is empty");
                if (!(new JavaScriptSerializer().DeserializeObject(text) is Dictionary<string, object> section))
                    throw new InvalidDataException("the file is not a JSON object");
                var firm = FromSection(section);
                firm.Managed = true;
                return firm;
            }
            catch (Exception ex)
            {
                (logError ?? Log.Error)($"Telemetry: ignoring the firm settings in '{firmFilePath}': {ex.Message}");
                return fallback;
            }
        }

        // ---- config.json shape --------------------------------------------------------

        /// <summary>Reads a parsed "telemetry" object; anything missing or of the wrong
        /// type keeps its default, never throws.</summary>
        public static TelemetryConfig FromSection(Dictionary<string, object> section)
        {
            var config = new TelemetryConfig();
            if (section == null) return config;
            if (section.TryGetValue("enabled", out var enabled) && enabled is bool on)
                config.Enabled = on;
            if (section.TryGetValue("folder", out var folder) && folder is string folderText)
                config.Folder = folderText.Trim();
            if (section.TryGetValue("url", out var url) && url is string urlText)
                config.Url = urlText.Trim();
            if (section.TryGetValue("includeHooks", out var hooks) && hooks is bool withHooks)
                config.IncludeHooks = withHooks;
            return config;
        }

        /// <summary>Writes the keys that differ from the defaults into
        /// <paramref name="section"/> and removes the ones that do not, leaving keys it
        /// does not know alone. Managed is never written.</summary>
        public void WriteSection(Dictionary<string, object> section)
        {
            if (Enabled) section["enabled"] = true;
            else section.Remove("enabled");
            SetOrRemove(section, "folder", Folder);
            SetOrRemove(section, "url", Url);
            if (IncludeHooks) section["includeHooks"] = true;
            else section.Remove("includeHooks");
        }

        private static void SetOrRemove(Dictionary<string, object> section, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) section.Remove(key);
            else section[key] = value.Trim();
        }
    }
}
