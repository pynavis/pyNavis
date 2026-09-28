using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure half of the Apply Speeds / Speeds to Saved bundles:
    /// extensions/pyNavis.extension/lib/speeds.py, imported through the real
    /// IronPython engine. Nothing in that module touches Navisworks, so
    /// importing it here runs no host code and moves no camera.
    /// </summary>
    public class SpeedsTests
    {
        private readonly IronPythonEngine _engine;

        public SpeedsTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(ExtensionLibDir());
            _engine.Initialize(config);
        }

        private static string ExtensionLibDir()
        {
            var candidate = Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension", "lib"));
            if (!Directory.Exists(candidate))
                throw new DirectoryNotFoundException("extension lib not found at " + candidate);
            return candidate;
        }

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "import speeds\n" + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        // ---- units: three spellings of the same thing ----------------------

        [Fact]
        public void MetresPer_AcceptsShortCodes_DropdownLabels_AndEnumNames()
        {
            // doc.Units stringifies to the enum name ("Feet"); an exported
            // viewpoints XML declares the short code ("ft"); the dropdown shows
            // the per-second label ("ft/s"). All three are the same unit.
            Run("assert speeds.metres_per('ft') == speeds.metres_per('ft/s')\n" +
                "assert speeds.metres_per('ft') == speeds.metres_per('Feet')\n" +
                "assert speeds.metres_per('in') == speeds.metres_per('Inches')\n" +
                "assert speeds.metres_per('m') == speeds.metres_per('Meters') == 1.0");
        }

        [Fact]
        public void MetresPer_CoversEveryNavisworksUnitsMember()
        {
            // The original add-in stopped at six and silently read a model in
            // kilometres as one in metres.
            Run("names = ['Meters', 'Centimeters', 'Millimeters', 'Feet', 'Inches',\n" +
                "         'Yards', 'Kilometers', 'Miles', 'Micrometers', 'Mils', 'Microinches']\n" +
                "seen = [speeds.metres_per(n) for n in names]\n" +
                "assert len(set(seen)) == len(names), seen\n" +
                "assert speeds.metres_per('Kilometers') == 1000.0\n" +
                "assert abs(speeds.metres_per('Miles') - 1609.344) < 1e-9\n" +
                "assert abs(speeds.metres_per('Mils') - 2.54e-05) < 1e-12");
        }

        [Fact]
        public void MetresPer_UnknownUnit_ReadsAsMetres_RatherThanRaising()
        {
            // A future Units member must not throw in the middle of a click.
            Run("assert speeds.metres_per('Furlongs') == 1.0\n" +
                "assert speeds.metres_per('') == 1.0\n" +
                "assert speeds.metres_per(None) == 1.0");
        }

        [Fact]
        public void ConvertLinear_RoundTrips_AndMatchesKnownFactors()
        {
            Run("assert speeds.convert_linear(30.0, 'm/s', 'm/s') == 30.0\n" +
                "feet = speeds.convert_linear(30.0, 'm/s', 'ft/s')\n" +
                "assert abs(feet - 98.4251968) < 1e-6, feet\n" +
                "back = speeds.convert_linear(feet, 'ft/s', 'm/s')\n" +
                "assert abs(back - 30.0) < 1e-9, back\n" +
                "inches = speeds.convert_linear(1.0, 'ft/s', 'in/s')\n" +
                "assert abs(inches - 12.0) < 1e-9, inches");
        }

        [Fact]
        public void ConvertLinear_CrossesSpellings_SoADocumentUnitReachesADropdownUnit()
        {
            Run("a = speeds.convert_linear(10.0, 'Feet', 'm/s')\n" +
                "b = speeds.convert_linear(10.0, 'ft/s', 'm/s')\n" +
                "assert a == b, (a, b)\n" +
                "assert abs(a - 3.048) < 1e-9, a");
        }

        // ---- angles --------------------------------------------------------

        [Fact]
        public void AngularRadians_ConvertsDegreesPerSecond()
        {
            Run("import math\n" +
                "assert abs(speeds.angular_radians(180.0) - math.pi) < 1e-12\n" +
                "assert speeds.angular_radians(0) == 0.0\n" +
                "assert abs(speeds.angular_radians(45.0) - math.pi / 4.0) < 1e-12");
        }

        [Fact]
        public void VerticalFov_AtAspectRatioOne_EqualsTheHorizontalAngle()
        {
            // A square viewport sees the same angle both ways, which is the one
            // case the trigonometry can be checked against by hand.
            Run("import math\n" +
                "v = speeds.vertical_fov(84.0, 1.0)\n" +
                "assert abs(math.degrees(v) - 84.0) < 1e-9, math.degrees(v)");
        }

        [Fact]
        public void VerticalFov_OnAWideViewport_IsNarrowerThanTheHorizontalAngle()
        {
            Run("import math\n" +
                "v = math.degrees(speeds.vertical_fov(84.0, 16.0 / 9.0))\n" +
                "assert 50.0 < v < 60.0, v");
        }

        [Fact]
        public void VerticalFov_UnusableInput_IsNone_SoTheCallerSkipsFovAndKeepsTheSpeeds()
        {
            Run("assert speeds.vertical_fov(84.0, 0.0) is None\n" +
                "assert speeds.vertical_fov(84.0, -1.5) is None\n" +
                "assert speeds.vertical_fov(84.0, float('inf')) is None\n" +
                "assert speeds.vertical_fov(84.0, float('nan')) is None\n" +
                "assert speeds.vertical_fov(84.0, None) is None\n" +
                "assert speeds.vertical_fov(84.0, 'wide') is None\n" +
                "assert speeds.vertical_fov(0.0, 1.5) is None\n" +
                "assert speeds.vertical_fov(180.0, 1.5) is None\n" +
                "assert speeds.vertical_fov(200.0, 1.5) is None");
        }

        // ---- reading what the user typed -----------------------------------

        [Fact]
        public void ParseNumber_TakesAPlainNumber_AndFallsBackOnAnythingElse()
        {
            Run("assert speeds.parse_number('30', 1.0) == 30.0\n" +
                "assert speeds.parse_number('  30.5  ', 1.0) == 30.5\n" +
                "assert speeds.parse_number('-2', 1.0) == -2.0\n" +
                "assert speeds.parse_number('', 7.0) == 7.0\n" +
                "assert speeds.parse_number('fast', 7.0) == 7.0\n" +
                "assert speeds.parse_number(None, 7.0) == 7.0\n" +
                "assert speeds.parse_number('nan', 7.0) == 7.0\n" +
                "assert speeds.parse_number('inf', 7.0) == 7.0");
        }

        // ---- settings repair -----------------------------------------------

        [Fact]
        public void Normalise_RepairsEveryFieldOfAHandEditedSettingsFile()
        {
            // The settings file is user-editable JSON, so nothing may assume
            // the types it finds.
            Run("v = speeds.normalise({'linear': 'fast', 'linear_unit': 'furlongs/s',\n" +
                "                       'change_linear': 'yes', 'angular': None,\n" +
                "                       'change_angular': 0, 'fov': 400.0, 'change_fov': 1})\n" +
                "assert v['linear'] == speeds.DEFAULTS['linear'], v\n" +
                "assert v['linear_unit'] == 'm/s', v\n" +
                "assert v['change_linear'] is True, v\n" +
                "assert v['angular'] == speeds.DEFAULTS['angular'], v\n" +
                "assert v['change_angular'] is False, v\n" +
                "assert v['fov'] == speeds.DEFAULTS['fov'], v\n" +
                "assert v['change_fov'] is True, v");
        }

        [Fact]
        public void Normalise_KeepsGoodValues_AndClampsNegativeSpeedsToZero()
        {
            Run("v = speeds.normalise({'linear': 12.5, 'linear_unit': 'ft/s',\n" +
                "                       'change_linear': False, 'angular': 90.0,\n" +
                "                       'change_angular': True, 'fov': 60.0, 'change_fov': False})\n" +
                "assert v['linear'] == 12.5 and v['linear_unit'] == 'ft/s'\n" +
                "assert v['change_linear'] is False and v['change_angular'] is True\n" +
                "assert v['angular'] == 90.0 and v['fov'] == 60.0\n" +
                "assert speeds.normalise({'linear': -5.0})['linear'] == 0.0\n" +
                "assert speeds.normalise({'angular': -5.0})['angular'] == 0.0");
        }

        [Fact]
        public void Normalise_OfDefaults_IsDefaults_AndTheKeysNeverDrift()
        {
            Run("v = speeds.normalise(dict(speeds.DEFAULTS))\n" +
                "assert v == speeds.DEFAULTS, v\n" +
                "assert sorted(v.keys()) == ['angular', 'change_angular', 'change_fov',\n" +
                "                            'change_linear', 'fov', 'linear', 'linear_unit']\n" +
                "assert speeds.DEFAULTS['linear_unit'] in speeds.UNITS");
        }

        [Fact]
        public void Normalise_OfAnEmptyDict_IsDefaults()
        {
            Run("assert speeds.normalise({}) == speeds.DEFAULTS");
        }

        // ---- what the toast says -------------------------------------------

        [Fact]
        public void Trim_DropsTrailingZeros_SoThirtyPrintsAsThirty()
        {
            Run("assert speeds.trim(30.0) == '30'\n" +
                "assert speeds.trim(30.5) == '30.5'\n" +
                "assert speeds.trim(0.0) == '0'\n" +
                "assert speeds.trim(98.42519685) == '98.4252'");
        }

        [Fact]
        public void Summary_NamesOnlyTheSwitchedOnValues()
        {
            Run("v = speeds.normalise(dict(speeds.DEFAULTS))\n" +
                "assert speeds.summary(v) == '30 m/s, 45 deg/sec, 84 deg FOV', speeds.summary(v)\n" +
                "v['change_angular'] = False\n" +
                "assert speeds.summary(v) == '30 m/s, 84 deg FOV', speeds.summary(v)");
        }

        [Fact]
        public void Summary_WithEverythingOff_IsEmpty_WhichIsTheCueToSayNothingWasReset()
        {
            Run("v = speeds.normalise(dict(speeds.DEFAULTS))\n" +
                "v['change_linear'] = v['change_angular'] = v['change_fov'] = False\n" +
                "assert speeds.summary(v) == '', repr(speeds.summary(v))");
        }

        [Fact]
        public void Summary_ContainsNoEmDash()
        {
            // Project rule: no em dashes in any user-facing string.
            Run("assert u'\\u2014' not in speeds.summary(speeds.normalise({}))");
        }

        // ---- engine compatibility -------------------------------------------

        [Fact]
        public void Module_ContainsNoEmDash()
        {
            Run("import os\n" +
                "path = os.path.join(os.path.dirname(speeds.__file__), 'speeds.py')\n" +
                "with open(path) as f:\n" +
                "    text = f.read()\n" +
                "assert u'\\u2014' not in text, 'em dash in speeds.py'");
        }
    }
}
