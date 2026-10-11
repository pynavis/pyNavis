using System;
using System.Collections.Generic;
using System.IO;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Telemetry;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Where telemetry settings come from: the user's "telemetry" section in
    /// config.json, unless the firm has put %PROGRAMDATA%\pyNavis\telemetry.json on
    /// the machine, in which case that file decides everything.
    /// </summary>
    public class TelemetryConfigTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _path;
        private readonly string _firm;
        private readonly List<string> _logged = new List<string>();

        public TelemetryConfigTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "config.json");
            _firm = Path.Combine(_dir, "programdata", "telemetry.json");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private void Given(string json) => File.WriteAllText(_path, json);

        private void GivenFirm(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_firm));
            File.WriteAllText(_firm, json);
        }

        private string Written() => File.ReadAllText(_path);

        // Trusted: whether a firm file may be trusted is TelemetryHostTests' subject.
        private TelemetryConfig Resolve(TelemetryConfig user) => TelemetryConfig.Resolve(user, _firm, _ => true, _logged.Add);

        [Fact]
        public void Defaults_AreOff_WithNowhereToSend()
        {
            var config = PyNavisConfig.Load(Path.Combine(_dir, "missing.json")).Telemetry;

            Assert.False(config.Enabled);
            Assert.Equal("", config.Folder);
            Assert.Equal("", config.Url);
            Assert.False(config.IncludeHooks);
            Assert.False(config.Managed);
            Assert.False(config.IsActive);
        }

        [Fact]
        public void UserSection_IsReadFromConfigJson()
        {
            Given(@"{ ""theme"": ""dark"", ""telemetry"": {
                ""enabled"": true,
                ""folder"": ""\\\\server\\share\\pynavis"",
                ""url"": "" https://telemetry.example.test/pynavis "",
                ""includeHooks"": true } }");

            var config = PyNavisConfig.Load(_path).Telemetry;

            Assert.True(config.Enabled);
            Assert.Equal(@"\\server\share\pynavis", config.Folder);
            Assert.Equal("https://telemetry.example.test/pynavis", config.Url);
            Assert.True(config.IncludeHooks);
            Assert.False(config.Managed);
            Assert.True(config.IsActive);
        }

        [Fact]
        public void UserSection_WithOddValues_KeepsTheDefaults()
        {
            Given(@"{ ""telemetry"": { ""enabled"": ""yes"", ""folder"": 3, ""includeHooks"": null } }");

            var config = PyNavisConfig.Load(_path).Telemetry;

            Assert.False(config.Enabled);
            Assert.Equal("", config.Folder);
            Assert.False(config.IncludeHooks);
        }

        [Fact]
        public void Enabled_WithNoFolderOrUrl_IsNotActive()
        {
            Assert.False(new TelemetryConfig { Enabled = true }.IsActive);
            Assert.True(new TelemetryConfig { Enabled = true, Url = "https://x.test" }.IsActive);
            Assert.False(new TelemetryConfig { Enabled = false, Folder = @"C:\t" }.IsActive);
        }

        [Fact]
        public void NoFirmFile_TheUsersSettingsStand()
        {
            var user = new TelemetryConfig { Enabled = true, Folder = @"C:\mine" };

            var resolved = Resolve(user);

            Assert.True(resolved.Enabled);
            Assert.Equal(@"C:\mine", resolved.Folder);
            Assert.False(resolved.Managed);
            Assert.Empty(_logged);
        }

        [Fact]
        public void FirmFile_WinsEntirely_AndMarksTheSettingsManaged()
        {
            GivenFirm(@"{ ""enabled"": true, ""url"": ""https://firm.example.test/t"" }");
            var user = new TelemetryConfig { Enabled = false, Folder = @"C:\mine", IncludeHooks = true };

            var resolved = Resolve(user);

            Assert.True(resolved.Enabled);
            Assert.Equal("", resolved.Folder);              // the user's folder does not leak through
            Assert.Equal("https://firm.example.test/t", resolved.Url);
            Assert.False(resolved.IncludeHooks);
            Assert.True(resolved.Managed);
            Assert.Empty(_logged);
        }

        [Fact]
        public void FirmFile_CanSwitchTelemetryOff_ForEveryone()
        {
            GivenFirm(@"{ ""enabled"": false }");

            var resolved = Resolve(new TelemetryConfig { Enabled = true, Folder = @"C:\mine" });

            Assert.False(resolved.IsActive);
            Assert.True(resolved.Managed);
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("[1, 2]")]
        [InlineData("")]
        public void MalformedFirmFile_IsLoggedAndIgnored(string json)
        {
            GivenFirm(json);
            var user = new TelemetryConfig { Enabled = true, Folder = @"C:\mine" };

            var resolved = Resolve(user);

            Assert.True(resolved.Enabled);
            Assert.Equal(@"C:\mine", resolved.Folder);
            Assert.False(resolved.Managed);
            var line = Assert.Single(_logged);
            Assert.Contains(_firm, line);
        }

        [Fact]
        public void Resolve_HandsBackACopy_NeverTheUsersObject()
        {
            var user = new TelemetryConfig { Enabled = true, Folder = @"C:\mine" };

            var resolved = Resolve(user);
            resolved.Folder = @"C:\other";

            Assert.Equal(@"C:\mine", user.Folder);
            Assert.False(Resolve(null).Enabled);
        }

        [Fact]
        public void DefaultFirmPath_IsUnderProgramData()
        {
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "pyNavis", "telemetry.json");
            Assert.Equal(expected, TelemetryConfig.DefaultFirmPath);
        }

        // ---- saving ----------------------------------------------------------------

        [Fact]
        public void SaveTelemetry_RoundTrips_AndKeepsEverythingElse()
        {
            Given(@"{ ""theme"": ""dark"", ""updates"": { ""check"": false },
                      ""telemetry"": { ""future"": 1 } }");
            var settings = new TelemetryConfig
            {
                Enabled = true,
                Folder = @"\\server\share\pynavis",
                Url = "https://telemetry.example.test/pynavis",
                IncludeHooks = true,
                Managed = true,                              // never written: it is where values came from
            };

            PyNavisConfig.SaveTelemetry(_path, settings);
            var back = PyNavisConfig.Load(_path);

            Assert.True(back.Telemetry.Enabled);
            Assert.Equal(@"\\server\share\pynavis", back.Telemetry.Folder);
            Assert.Equal("https://telemetry.example.test/pynavis", back.Telemetry.Url);
            Assert.True(back.Telemetry.IncludeHooks);
            Assert.False(back.Telemetry.Managed);
            Assert.Equal("dark", back.Theme);
            Assert.False(back.UpdatesCheck);
            Assert.Contains("\"future\"", Written());
            Assert.DoesNotContain("managed", Written(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SaveTelemetry_WritesOnlyWhatDiffersFromTheDefaults()
        {
            Given(@"{ ""theme"": ""dark"", ""telemetry"": { ""enabled"": true, ""folder"": ""C:\\t"" } }");

            PyNavisConfig.SaveTelemetry(_path, new TelemetryConfig());

            Assert.DoesNotContain("telemetry", Written());
            Assert.Equal("dark", PyNavisConfig.Load(_path).Theme);

            PyNavisConfig.SaveTelemetry(_path, new TelemetryConfig { Url = "https://x.test" });
            Assert.DoesNotContain("enabled", Written());
            Assert.DoesNotContain("includeHooks", Written());
            Assert.Contains("https://x.test", Written());
        }

        [Fact]
        public void SaveTelemetry_RefusesToOverwriteABrokenFile()
        {
            Given("{ broken");

            Assert.Throws<InvalidDataException>(
                () => PyNavisConfig.SaveTelemetry(_path, new TelemetryConfig { Enabled = true }));
            Assert.Equal("{ broken", Written());
        }

        // ---- the Settings window's save --------------------------------------------

        [Fact]
        public void UserSettings_CarryTheTelemetrySection_AndSaveItBack()
        {
            Given(@"{ ""telemetry"": { ""enabled"": true, ""folder"": ""C:\\t"" } }");

            var settings = PyNavisConfig.Load(_path).ToUserSettings();
            Assert.True(settings.Telemetry.Enabled);
            Assert.Equal(@"C:\t", settings.Telemetry.Folder);

            settings.Telemetry.IncludeHooks = true;
            PyNavisConfig.SaveUserSettings(_path, settings);

            var back = PyNavisConfig.Load(_path).Telemetry;
            Assert.True(back.Enabled);
            Assert.Equal(@"C:\t", back.Folder);
            Assert.True(back.IncludeHooks);
        }

        [Fact]
        public void UserSettings_WithoutTelemetry_LeaveTheSectionAlone()
        {
            // A window that does not show telemetry hands back null: saving must not
            // reset the section to the defaults.
            Given(@"{ ""telemetry"": { ""enabled"": true, ""folder"": ""C:\\t"" } }");

            PyNavisConfig.SaveUserSettings(_path, new PyNavisConfig.UserSettings());

            var back = PyNavisConfig.Load(_path).Telemetry;
            Assert.True(back.Enabled);
            Assert.Equal(@"C:\t", back.Folder);
        }
    }
}
