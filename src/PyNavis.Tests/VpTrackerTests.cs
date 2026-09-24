using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure half of the Viewpoint Tracker panel and window:
    /// extensions/pyNavis.extension/lib/vptracker.py, imported through the real
    /// IronPython engine. It decides whether the camera is still on the saved
    /// view and what the two lines say; nothing in it touches Navisworks.
    /// </summary>
    public class VpTrackerTests
    {
        private readonly IronPythonEngine _engine;

        public VpTrackerTests()
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
                Code = "import vptracker\n" + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        // ---- is the camera still where the saved view put it? ---------------

        [Fact]
        public void Matches_IdenticalCamera_IsActive()
        {
            Run("p = (10.0, 20.0, 30.0)\n" +
                "q = (0.0, 0.0, 0.7071, 0.7071)\n" +
                "assert vptracker.matches(p, q, p, q) is True");
        }

        [Fact]
        public void Matches_AFloatRoundTripApart_IsStillActive()
        {
            // Recalling a view and reading the camera back moves the numbers in
            // the last decimal places. An exact compare would report "walked
            // away" the instant a view was recalled.
            Run("p = (10.0, 20.0, 30.0)\n" +
                "q = (0.0, 0.0, 0.7071, 0.7071)\n" +
                "near_p = (10.001, 19.999, 30.0009)\n" +
                "near_q = (0.0005, 0.0, 0.70712, 0.70708)\n" +
                "assert vptracker.matches(near_p, near_q, p, q) is True");
        }

        [Fact]
        public void Matches_MovedFurtherThanTheTolerance_IsNotActive()
        {
            Run("p = (10.0, 20.0, 30.0)\n" +
                "q = (0.0, 0.0, 0.7071, 0.7071)\n" +
                "assert vptracker.matches((10.5, 20.0, 30.0), q, p, q) is False\n" +
                "assert vptracker.matches(p, (0.2, 0.0, 0.7071, 0.7071), p, q) is False");
        }

        [Fact]
        public void Matches_ExactlyAtTheTolerance_IsNotActive()
        {
            // The test is strict "less than", so the boundary reads as moved.
            Run("p = (0.0, 0.0, 0.0)\n" +
                "q = (0.0, 0.0, 0.0, 1.0)\n" +
                "assert vptracker.matches((vptracker.TOLERANCE, 0.0, 0.0), q, p, q) is False");
        }

        [Fact]
        public void Matches_OrbitThatLeavesPositionAlone_IsStillCaughtByTheRotation()
        {
            // Orbiting around a point can leave Position untouched, so the
            // quaternion is the only thing that reports it.
            Run("p = (10.0, 20.0, 30.0)\n" +
                "assert vptracker.matches(p, (0.0, 0.0, 0.0, 1.0),\n" +
                "                         p, (0.0, 0.0, 0.7071, 0.7071)) is False");
        }

        [Fact]
        public void Matches_MissingOrMalformedInput_ReadsAsMoved()
        {
            // Claiming a view is still active when it cannot be checked is the
            // worse of the two errors: the panel would lie.
            Run("p = (10.0, 20.0, 30.0)\n" +
                "q = (0.0, 0.0, 0.7071, 0.7071)\n" +
                "assert vptracker.matches(None, q, p, q) is False\n" +
                "assert vptracker.matches(p, None, p, q) is False\n" +
                "assert vptracker.matches(p, q, None, None) is False\n" +
                "assert vptracker.matches((10.0, 20.0), q, p, q) is False\n" +
                "assert vptracker.matches(p, (0.0, 0.0, 1.0), p, q) is False\n" +
                "assert vptracker.matches(('x', 'y', 'z'), q, p, q) is False");
        }

        [Fact]
        public void Matches_NaN_ReadsAsMoved_NotAsAMatch()
        {
            Run("nan = float('nan')\n" +
                "p = (10.0, 20.0, 30.0)\n" +
                "q = (0.0, 0.0, 0.7071, 0.7071)\n" +
                "assert vptracker.matches((nan, 20.0, 30.0), q, p, q) is False\n" +
                "assert vptracker.matches(p, q, (nan, 20.0, 30.0), q) is False");
        }

        [Fact]
        public void Matches_TakesAWiderTolerance_WhenOneIsGiven()
        {
            Run("p = (0.0, 0.0, 0.0)\n" +
                "q = (0.0, 0.0, 0.0, 1.0)\n" +
                "assert vptracker.matches((0.5, 0.0, 0.0), q, p, q) is False\n" +
                "assert vptracker.matches((0.5, 0.0, 0.0), q, p, q, tolerance=1.0) is True");
        }

        // ---- the two lines the panel shows ----------------------------------

        [Fact]
        public void DisplayName_FallsBackToTheStandingMessage()
        {
            Run("assert vptracker.display_name('Level 2 Corridor') == 'Level 2 Corridor'\n" +
                "assert vptracker.display_name('  Trimmed  ') == 'Trimmed'\n" +
                "assert vptracker.display_name(None) == vptracker.NOTHING\n" +
                "assert vptracker.display_name('') == vptracker.NOTHING\n" +
                "assert vptracker.display_name('   ') == vptracker.NOTHING");
        }

        [Fact]
        public void StatusLine_SaysActiveOnTheView_AndLastOnceYouWalkOff()
        {
            Run("assert vptracker.status_line('Level 2', True) == vptracker.ACTIVE\n" +
                "assert vptracker.status_line('Level 2', False) == vptracker.LAST");
        }

        [Fact]
        public void StatusLine_WithNothingTracked_IsEmpty_SoNoLabelHangsOverNothing()
        {
            Run("assert vptracker.status_line(None, True) == ''\n" +
                "assert vptracker.status_line('', False) == ''\n" +
                "assert vptracker.status_line('   ', True) == ''\n" +
                "assert vptracker.status_line(vptracker.NOTHING, True) == ''");
        }

        [Fact]
        public void TheThreeLines_AreSentenceCase_AndContainNoEmDash()
        {
            // The design system is explicit: "Sentence case everywhere. No em
            // dashes, no caps." An all-caps status badge reads as a convention
            // borrowed from somewhere else, so it is pinned here rather than
            // left to whoever edits these constants next.
            Run("for text in (vptracker.NOTHING, vptracker.ACTIVE, vptracker.LAST):\n" +
                "    assert u'\\u2014' not in text, text\n" +
                "    assert text[:1] == text[:1].upper(), text\n" +
                "    assert text != text.upper(), 'all caps: ' + text");
        }
    }
}
