using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PyNavis.Runtime.Update
{
    /// <summary>A published release, as the update check needs it.</summary>
    public sealed class Release
    {
        /// <summary>"1.2.1", from the tag "v1.2.1".</summary>
        public string Version { get; set; }

        /// <summary>The release page, for people who would rather download it themselves.</summary>
        public string Page { get; set; }

        public string InstallerName { get; set; }

        /// <summary>The setup exe; null when the release has none (notify only).</summary>
        public string InstallerUrl { get; set; }

        public long InstallerSize { get; set; }

        /// <summary>The setup exe's SHA-256: GitHub's own digest of the asset, or the one
        /// the release notes print. Null when neither exists, and then nothing is run.</summary>
        public string Sha256 { get; set; }
    }

    /// <summary>
    /// The pure half of the update check. pyNavis asks GitHub for its latest release
    /// once a day (one anonymous request; nothing about the user is sent), compares
    /// it with the running version, and says so. UpdateService does the network, the
    /// download and running the installer when Navisworks closes.
    /// </summary>
    public static class UpdateCheck
    {
        public const string LatestReleaseApi = "https://api.github.com/repos/pynavis/pyNavis/releases/latest";

        /// <summary>How long after a check the next one is due: a day, less a few hours so
        /// a morning start does not wait for yesterday's afternoon.</summary>
        public static readonly TimeSpan Interval = TimeSpan.FromHours(20);

        private static readonly Regex InstallerName = new Regex(@"^pyNavis-\d+\.\d+\.\d+-setup\.exe$", RegexOptions.IgnoreCase);
        private static readonly Regex NotesSha = new Regex(@"sha-?256\W*([0-9a-f]{64})", RegexOptions.IgnoreCase);

        /// <summary>[major, minor, patch] from "1.2.3" or "v1.2.3"; null otherwise.</summary>
        public static int[] ParseVersion(string text)
        {
            var t = (text ?? "").Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t.Substring(1);
            var parts = t.Split('.');
            if (parts.Length != 3) return null;
            var numbers = new int[3];
            for (var i = 0; i < 3; i++)
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    return null;
            return numbers;
        }

        /// <summary>Numeric comparison; anything that is not a version sorts lowest.</summary>
        public static int CompareVersions(string a, string b)
        {
            var x = ParseVersion(a);
            var y = ParseVersion(b);
            if (x == null || y == null) return (x == null ? 0 : 1) - (y == null ? 0 : 1);
            for (var i = 0; i < 3; i++)
                if (x[i] != y[i]) return x[i].CompareTo(y[i]);
            return 0;
        }

        /// <summary>GitHub's latest-release JSON as a Release, or null when it is not one
        /// (a rate-limit message, a tag that is not a version, anything unreadable).</summary>
        public static Release ParseRelease(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            Dictionary<string, object> data;
            try { data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); }
            catch (Exception) { return null; }
            if (data == null) return null;

            var tag = Text(data, "tag_name");
            if (ParseVersion(tag) == null) return null;
            var release = new Release
            {
                Version = tag.TrimStart('v', 'V'),
                Page = Text(data, "html_url"),
            };

            if (data.TryGetValue("assets", out var raw) && raw is IEnumerable assets)
            {
                foreach (var item in assets)
                {
                    if (!(item is Dictionary<string, object> asset)) continue;
                    var name = Text(asset, "name");
                    if (name == null || !InstallerName.IsMatch(name)) continue;
                    release.InstallerName = name;
                    release.InstallerUrl = Text(asset, "browser_download_url");
                    release.InstallerSize = asset.TryGetValue("size", out var s)
                        && long.TryParse(Convert.ToString(s, CultureInfo.InvariantCulture), out var n) ? n : 0;
                    var digest = Text(asset, "digest");
                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        release.Sha256 = digest.Substring(7).ToLowerInvariant();
                    break;
                }
            }
            if (release.Sha256 == null)
            {
                var match = NotesSha.Match(Text(data, "body") ?? "");
                if (match.Success) release.Sha256 = match.Groups[1].Value.ToLowerInvariant();
            }
            return release;
        }

        /// <summary>A check is due once a day, and whenever the last one looks to be in the
        /// future (a clock put back).</summary>
        public static bool Due(DateTime nowUtc, DateTime? lastUtc) =>
            lastUtc == null || lastUtc.Value > nowUtc || nowUtc - lastUtc.Value >= Interval;

        /// <summary>The release is newer than what runs, and not the version the user skipped.</summary>
        public static bool IsNewer(string installed, Release release, string skipped) =>
            release != null
            && CompareVersions(release.Version, installed) > 0
            && CompareVersions(release.Version, skipped) != 0;

        public static (string title, string detail) ReadyMessage(Release release) =>
            ($"pyNavis {release.Version} is ready",
             "It installs when you close Navisworks. Settings > Updates to skip it or turn this off.");

        public static (string title, string detail) AvailableMessage(Release release) =>
            ($"pyNavis {release.Version} is available", "Settings > Updates opens the download page.");

        /// <summary>The line Settings > Updates shows. latest is the newest release the last
        /// check saw, ready the version whose installer is downloaded and verified.</summary>
        public static string StatusLine(string installed, string latest, string ready, string skipped,
            DateTime? lastCheckLocal)
        {
            if (latest == null || lastCheckLocal == null) return "Not checked yet.";
            if (CompareVersions(latest, installed) <= 0)
                return $"You have the latest version, {installed}. Checked " +
                       lastCheckLocal.Value.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture) + ".";
            if (CompareVersions(latest, skipped) == 0) return $"pyNavis {latest} is out; you chose to skip it.";
            if (CompareVersions(latest, ready) == 0)
                return $"pyNavis {latest} is downloaded and installs when you close Navisworks.";
            return $"pyNavis {latest} is available.";
        }

        /// <summary>A downloaded installer is kept only while it is newer than what runs.</summary>
        public static bool ShouldDiscard(string installed, string installerVersion) =>
            installerVersion == null || CompareVersions(installerVersion, installed) <= 0;

        /// <summary>Quiet, no questions, and only once the Navisworks that launched it has
        /// exited (the installer waits on /WAITPID), with a log to diagnose from.</summary>
        public static string InstallerArguments(int pid, string logPath) =>
            $"/SILENT /SUPPRESSMSGBOXES /NORESTART /WAITPID={pid} /LOG=\"{logPath}\"";

        private static string Text(Dictionary<string, object> data, string key) =>
            data.TryGetValue(key, out var value) ? value as string : null;
    }
}
