using System;
using PyNavis.Runtime.Update;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure half of the update check: reading GitHub's latest release, comparing
    /// versions, when a check is due, and what to say. The network, the download
    /// and running the installer when Navisworks closes are UpdateService.
    /// </summary>
    public class UpdateCheckTests
    {
        private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string BodySha = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

        private static string ReleaseJson(string tag = "v1.2.1", bool withDigest = true, bool withAsset = true) =>
            "{\"tag_name\":\"" + tag + "\",\"name\":\"pyNavis 1.2.1\"," +
            "\"html_url\":\"https://github.com/pynavis/pyNavis/releases/tag/" + tag + "\"," +
            "\"draft\":false,\"prerelease\":false," +
            "\"body\":\"Fixes.\\r\\n\\r\\nSetup SHA256: " + BodySha + "\"," +
            "\"assets\":[" + (withAsset
                ? "{\"name\":\"pyNavis-1.2.1-setup.exe\",\"size\":31000000," +
                  "\"browser_download_url\":\"https://github.com/pynavis/pyNavis/releases/download/v1.2.1/pyNavis-1.2.1-setup.exe\"" +
                  (withDigest ? ",\"digest\":\"sha256:" + Digest + "\"" : "") + "}"
                : "") + "]}";

        [Fact]
        public void ParseRelease_TakesTheVersionPageAndInstaller_WithGitHubsDigest()
        {
            var release = UpdateCheck.ParseRelease(ReleaseJson());
            Assert.Equal("1.2.1", release.Version);
            Assert.Equal("https://github.com/pynavis/pyNavis/releases/tag/v1.2.1", release.Page);
            Assert.Equal("pyNavis-1.2.1-setup.exe", release.InstallerName);
            Assert.EndsWith("/pyNavis-1.2.1-setup.exe", release.InstallerUrl);
            Assert.Equal(31000000, release.InstallerSize);
            Assert.Equal(Digest, release.Sha256);
        }

        [Fact]
        public void ParseRelease_FallsBackToTheSha256InTheNotes_AndCopesWithNoInstaller()
        {
            Assert.Equal(BodySha, UpdateCheck.ParseRelease(ReleaseJson(withDigest: false)).Sha256);

            var bare = UpdateCheck.ParseRelease(ReleaseJson(withAsset: false));
            Assert.Equal("1.2.1", bare.Version);
            Assert.Null(bare.InstallerUrl);                // notify only: nothing to download
        }

        [Fact]
        public void ParseRelease_RefusesWhatIsNotARelease()
        {
            Assert.Null(UpdateCheck.ParseRelease(ReleaseJson(tag: "nightly")));
            Assert.Null(UpdateCheck.ParseRelease("{\"message\":\"API rate limit exceeded\"}"));
            Assert.Null(UpdateCheck.ParseRelease("not json"));
            Assert.Null(UpdateCheck.ParseRelease(null));
        }

        [Fact]
        public void CompareVersions_ByNumberNotByText()
        {
            Assert.True(UpdateCheck.CompareVersions("1.10.0", "1.9.9") > 0);
            Assert.True(UpdateCheck.CompareVersions("v1.2.1", "1.2.0") > 0);
            Assert.Equal(0, UpdateCheck.CompareVersions("1.2.0", "v1.2.0"));
            Assert.True(UpdateCheck.CompareVersions("2.0.0", "10.0.0") < 0);
            Assert.True(UpdateCheck.CompareVersions("1.2.0", "junk") > 0);   // junk sorts lowest
        }

        [Fact]
        public void Due_OncePerDay_AndWhenTheClockWentBack()
        {
            var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(UpdateCheck.Due(now, null));
            Assert.False(UpdateCheck.Due(now, now.AddHours(-3)));
            Assert.True(UpdateCheck.Due(now, now.AddHours(-21)));
            Assert.True(UpdateCheck.Due(now, now.AddDays(2)));             // a last check in the future
        }

        [Fact]
        public void IsNewer_UnlessSkipped()
        {
            var release = UpdateCheck.ParseRelease(ReleaseJson());
            Assert.True(UpdateCheck.IsNewer("1.2.0", release, null));
            Assert.False(UpdateCheck.IsNewer("1.2.1", release, null));
            Assert.False(UpdateCheck.IsNewer("1.3.0", release, null));
            Assert.False(UpdateCheck.IsNewer("1.2.0", release, "1.2.1"));
            Assert.True(UpdateCheck.IsNewer("1.2.0", release, "1.2.0"));   // an older skip does not hide a newer one
            Assert.False(UpdateCheck.IsNewer("1.2.0", null, null));
        }

        [Fact]
        public void Messages_SayWhatHappensNext()
        {
            var release = UpdateCheck.ParseRelease(ReleaseJson());
            Assert.Equal(("pyNavis 1.2.1 is ready",
                          "It installs when you close Navisworks. Settings > Updates to skip it or turn this off."),
                UpdateCheck.ReadyMessage(release));
            Assert.Equal(("pyNavis 1.2.1 is available",
                          "Settings > Updates opens the download page."),
                UpdateCheck.AvailableMessage(release));
        }

        [Fact]
        public void StatusLine_SaysWhereThingsStand()
        {
            var at = new DateTime(2026, 9, 29, 9, 5, 0);
            Assert.Equal("Not checked yet.", UpdateCheck.StatusLine("1.2.0", null, null, null, null));
            Assert.Equal("You have the latest version, 1.2.0. Checked 29 Sep 2026 09:05.",
                UpdateCheck.StatusLine("1.2.0", "1.2.0", null, null, at));
            Assert.Equal("pyNavis 1.2.1 is available.",
                UpdateCheck.StatusLine("1.2.0", "1.2.1", null, null, at));
            Assert.Equal("pyNavis 1.2.1 is downloaded and installs when you close Navisworks.",
                UpdateCheck.StatusLine("1.2.0", "1.2.1", "1.2.1", null, at));
            Assert.Equal("pyNavis 1.2.1 is out; you chose to skip it.",
                UpdateCheck.StatusLine("1.2.0", "1.2.1", "1.2.1", "1.2.1", at));
        }

        [Fact]
        public void ADownloadedInstaller_IsKeptOnlyWhileItIsNewerThanWhatRuns()
        {
            // Once it has installed (or something newer has), the file is clutter.
            Assert.False(UpdateCheck.ShouldDiscard("1.2.0", "1.2.1"));
            Assert.True(UpdateCheck.ShouldDiscard("1.2.1", "1.2.1"));
            Assert.True(UpdateCheck.ShouldDiscard("1.3.0", "1.2.1"));
            Assert.True(UpdateCheck.ShouldDiscard("1.2.0", null));
        }

        [Fact]
        public void InstallerArguments_RunQuietlyAfterNavisworksHasGone()
        {
            Assert.Equal("/SILENT /SUPPRESSMSGBOXES /NORESTART /WAITPID=4242 /LOG=\"C:\\x\\install.log\"",
                UpdateCheck.InstallerArguments(4242, @"C:\x\install.log"));
        }
    }
}
