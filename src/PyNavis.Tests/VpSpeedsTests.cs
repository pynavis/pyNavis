using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure half of Speeds to Saved: extensions/pyNavis.extension/lib/vpspeeds.py,
    /// imported through the real IronPython engine. Only the parts that decide WHICH
    /// saved viewpoints get written are testable here; the writing itself needs a live
    /// document and is covered by the in-app checklist.
    /// </summary>
    public class VpSpeedsTests
    {
        private readonly IronPythonEngine _engine;

        public VpSpeedsTests()
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

        /// <summary>A snapshot shaped exactly as pynavis.viewpoints.snapshot() returns:
        /// a folder, two viewpoints inside it, a top level viewpoint and an animation.</summary>
        private const string Snapshot =
            "snap = [\n" +
            "  {'guid': 'f', 'key': '0', 'parent_key': '', 'folder': '', 'name': 'Site',\n" +
            "   'kind': 'folder', 'depth': 0, 'is_folder': True, 'comments': 0},\n" +
            "  {'guid': 'a', 'key': '0/0', 'parent_key': '0', 'folder': 'Site', 'name': 'North',\n" +
            "   'kind': 'viewpoint', 'depth': 1, 'is_folder': False, 'comments': 2},\n" +
            "  {'guid': 'b', 'key': '0/1', 'parent_key': '0', 'folder': 'Site', 'name': 'South',\n" +
            "   'kind': 'viewpoint', 'depth': 1, 'is_folder': False, 'comments': 0},\n" +
            "  {'guid': 'c', 'key': '1', 'parent_key': '', 'folder': '', 'name': 'Lobby',\n" +
            "   'kind': 'viewpoint', 'depth': 0, 'is_folder': False, 'comments': 0},\n" +
            "  {'guid': 'd', 'key': '2', 'parent_key': '', 'folder': '', 'name': 'Walk',\n" +
            "   'kind': 'animation', 'depth': 0, 'is_folder': False, 'comments': 0},\n" +
            "]\n";

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "import vpspeeds\n" + Snapshot + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        // ---- which rows actually get written --------------------------------

        [Fact]
        public void Targets_KeepsOnlyTheViewpoints_InTreeOrder()
        {
            Run("got = [r['name'] for r in vpspeeds.targets(snap, ['a', 'b', 'c'])]\n" +
                "assert got == ['North', 'South', 'Lobby'], got");
        }

        [Fact]
        public void Targets_DropsFolders_BecauseAFolderHasNoCameraOfItsOwn()
        {
            Run("got = [r['name'] for r in vpspeeds.targets(snap, ['f', 'a'])]\n" +
                "assert got == ['North'], got");
        }

        [Fact]
        public void Targets_DropsAnimations_BecauseTheirCamerasLiveOnTheirCuts()
        {
            // Writing to the animation itself would report success and change
            // nothing the user can see.
            Run("got = [r['name'] for r in vpspeeds.targets(snap, ['d', 'c'])]\n" +
                "assert got == ['Lobby'], got");
        }

        [Fact]
        public void Targets_IgnoresGuidsThatAreNotInTheSnapshot()
        {
            Run("got = [r['name'] for r in vpspeeds.targets(snap, ['a', 'gone'])]\n" +
                "assert got == ['North'], got\n" +
                "assert vpspeeds.targets(snap, []) == []\n" +
                "assert vpspeeds.targets([], ['a']) == []");
        }

        [Fact]
        public void Targets_PreservesSnapshotOrder_NotTheOrderTheGuidsArrivedIn()
        {
            // The dialog hands back guids in selection order; writing in tree order
            // keeps the progress bar and the log matching what the user sees.
            Run("got = [r['name'] for r in vpspeeds.targets(snap, ['c', 'a', 'b'])]\n" +
                "assert got == ['North', 'South', 'Lobby'], got");
        }

        // ---- what was picked but cannot be written --------------------------

        [Fact]
        public void DroppedKinds_CountsFoldersAndAnimationsSeparately()
        {
            // The caller reports these, so ticking a folder never silently writes
            // to fewer viewpoints than the user thought.
            Run("got = vpspeeds.dropped_kinds(snap, ['f', 'd', 'a'])\n" +
                "assert got == {'folder': 1, 'animation': 1}, got");
        }

        [Fact]
        public void DroppedKinds_IsZeroWhenOnlyViewpointsWerePicked()
        {
            Run("got = vpspeeds.dropped_kinds(snap, ['a', 'b', 'c'])\n" +
                "assert got == {'folder': 0, 'animation': 0}, got\n" +
                "assert vpspeeds.dropped_kinds(snap, []) == {'folder': 0, 'animation': 0}");
        }

        [Fact]
        public void TargetsAndDroppedKinds_TogetherAccountForEveryPickedGuid()
        {
            // Nothing may vanish between the two: every tick is either written or
            // reported as skipped.
            Run("picked = ['f', 'a', 'b', 'c', 'd']\n" +
                "written = len(vpspeeds.targets(snap, picked))\n" +
                "skipped = vpspeeds.dropped_kinds(snap, picked)\n" +
                "assert written + skipped['folder'] + skipped['animation'] == len(picked)");
        }

        // ---- the route names reach the caller -------------------------------

        [Fact]
        public void BothRouteNames_ReadAsMidSentenceFragments_WithNoEmDash()
        {
            // These are not sentences: they land inside one, as "reset 5
            // viewpoints, keeping comments and overrides". So they must NOT start
            // capitalised, which is the opposite of the rule for a toast title and
            // is worth pinning rather than leaving to whoever edits them next.
            Run("for text in (vpspeeds.FULL_COPY, vpspeeds.REBUILT):\n" +
                "    assert text, 'route name is empty'\n" +
                "    assert u'\\u2014' not in text, text\n" +
                "    assert text != text.upper(), 'all caps: ' + text\n" +
                "    assert text[:1] == text[:1].lower(), 'not a fragment: ' + text");
        }

        [Fact]
        public void BothSpeedButtons_ReadTheAspectRatio_ThroughTheSameHelper()
        {
            // The two buttons once wrote lenses differing by a fraction of a degree
            // for the same setting, because one refreshed the viewport aspect ratio
            // before reading it and the other read it cold. The fix was one shared
            // helper; this fails if either script goes back to reading AspectRatio
            // directly, which is exactly how they drifted apart the first time.
            var root = Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension"));
            var speedsButton = Path.Combine(root, "pyNavis.tab", "Viewpoints.panel",
                "04_Speeds.stack", "01_Apply_Speeds.pushbutton", "script.py");
            var applier = Path.Combine(root, "lib", "vpspeeds.py");

            foreach (var path in new[] { speedsButton, applier })
            {
                var text = File.ReadAllText(path);
                Assert.True(text.Contains("aspect_ratio("),
                    Path.GetFileName(path) + " no longer uses the shared aspect_ratio helper");
                Assert.False(text.Contains(".AspectRatio"),
                    Path.GetFileName(path) + " reads AspectRatio directly again");
            }
        }

        [Fact]
        public void TheTwoRoutes_AreDistinct_SoTheReportCanTellThemApart()
        {
            // The caller decides whether to warn about lost redlines by comparing
            // against REBUILT, so the two must never collapse into one string.
            Run("assert vpspeeds.FULL_COPY != vpspeeds.REBUILT");
        }
    }
}
