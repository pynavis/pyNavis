using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure half of extensions/pyNavis.extension/lib/sectionnudge.py, the
    /// behaviour the Section Nudge panel and its six chord bundles share:
    /// session state, what each action does, which key means which action,
    /// and the readout words. Imported through the real IronPython engine;
    /// nothing here touches Navisworks.
    /// </summary>
    public class SectionNudgeTests
    {
        private readonly IronPythonEngine _engine;

        public SectionNudgeTests()
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

        private const string Prelude =
            "import sectionnudge as sn\n" +
            "def close(a, b, tol=1e-9):\n" +
            "    return abs(a - b) <= tol\n" +
            "def snapshot(enabled=True, mode='planes'):\n" +
            "    planes = [{'enabled': i < 4, 'origin': (float(i), 0.0, 0.0),\n" +
            "               'normal': (1.0, 0.0, 0.0), 'distance': float(i) * 10} for i in range(6)]\n" +
            "    if mode == 'box':\n" +
            "        planes = [{'enabled': True, 'normal': n, 'origin': (0.0, 0.0, 0.0), 'distance': 0.0}\n" +
            "                  for name, n in sn.FACES]\n" +
            "    return {'enabled': enabled, 'mode': mode, 'planes': planes,\n" +
            "            'box': {'min': (0.0, 0.0, 0.0), 'max': (4.0, 2.0, 1.0)}}\n";

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = Prelude + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void Normalize_RepairsAnythingTheSessionHolds()
        {
            Run("assert sn.normalize(None) == {'target': 'box', 'step_mm': 100.0, 'moves': 'world'}\n" +
                "assert sn.normalize({'target': 9, 'step_mm': -5}) == {'target': 'box', 'step_mm': 100.0, 'moves': 'world'}\n" +
                "assert sn.normalize({'target': 3, 'step_mm': 25}) == {'target': 3, 'step_mm': 25.0, 'moves': 'world'}\n" +
                "assert sn.normalize({'target': '2'}) == {'target': 2, 'step_mm': 100.0, 'moves': 'world'}\n" +
                "assert sn.normalize({'moves': 'screen'})['moves'] == 'screen'\n" +
                "assert sn.normalize({'moves': 'sideways'})['moves'] == 'world'");
        }

        [Fact]
        public void TargetCycling_WrapsBothWays_AndDirectPicksWork()
        {
            Run("state = sn.normalize(None)\n" +
                "order = ['box']\n" +
                "for _ in range(7):\n" +
                "    state = sn.plan(state, 'next')['state']\n" +
                "    order.append(state['target'])\n" +
                "assert order == ['box', 0, 1, 2, 3, 4, 5, 'box'], order\n" +
                "assert sn.plan(sn.normalize(None), 'prev')['state']['target'] == 5\n" +
                "assert sn.plan(state, 'target:2')['state']['target'] == 2\n" +
                "assert sn.plan(state, 'target:box')['state']['target'] == 'box'");
        }

        [Fact]
        public void Step_DoublesHalvesAndParses_WithinBounds()
        {
            Run("state = sn.normalize(None)\n" +
                "assert sn.plan(state, 'double')['state']['step_mm'] == 200.0\n" +
                "assert sn.plan(state, 'halve')['state']['step_mm'] == 50.0\n" +
                "assert sn.plan({'target': 'box', 'step_mm': 0.15}, 'halve')['state']['step_mm'] == sn.MIN_STEP_MM\n" +
                "assert sn.plan({'target': 'box', 'step_mm': 6e6}, 'double')['state']['step_mm'] == sn.MAX_STEP_MM\n" +
                "assert sn.plan(state, 'step:250')['state']['step_mm'] == 250.0\n" +
                "assert sn.plan(state, 'step:abc')['state']['step_mm'] == 100.0\n" +
                "assert sn.plan(state, 'step:0')['state']['step_mm'] == 100.0\n" +
                "assert sn.plan(state, 'step:abc')['problem'] == 'That is not a distance'");
        }

        [Fact]
        public void PlaneNudges_MoveAlongTheNormal_InDocumentUnits()
        {
            Run("state = {'target': 2, 'step_mm': 100.0}\n" +
                "assert sn.plan(state, 'in', per_mm=0.001)['edit'] == {'kind': 'plane', 'index': 2, 'distance': 0.1}\n" +
                "assert sn.plan(state, 'out', per_mm=0.001)['edit'] == {'kind': 'plane', 'index': 2, 'distance': -0.1}\n" +
                "r = sn.plan(state, 'left', per_mm=0.001)\n" +
                "assert r['edit'] is None and r['problem'], r");
        }

        [Fact]
        public void BoxMoves_FollowTheScreen_ThroughTheFrame_AndInOutMeanDownAndUp()
        {
            Run("state = {'target': 'box', 'step_mm': 250.0}\n" +
                "plan = sn.frame_for(None)\n" +                                  // no camera: plan view
                "for action, vector in (('right', (0.25, 0, 0)), ('left', (-0.25, 0, 0)),\n" +
                "                       ('up', (0, 0.25, 0)), ('down', (0, -0.25, 0)),\n" +
                "                       ('nearer', (0, 0, 0.25)), ('farther', (0, 0, -0.25))):\n" +
                "    edit = sn.plan(state, action, per_mm=0.001, frame=plan)['edit']\n" +
                "    assert edit['kind'] == 'box'\n" +
                "    assert all(close(a, b) for a, b in zip(edit['vector'], vector)), (action, edit)\n" +
                "north = sn.frame_for((0.7071067811865476, 0.0, 0.0, 0.7071067811865476))\n" +
                "edit = sn.plan(state, 'up', per_mm=0.001, frame=north)['edit']\n" +
                "assert all(close(a, b) for a, b in zip(edit['vector'], (0, 0, 0.25))), edit\n" +
                "edit = sn.plan(state, 'nearer', per_mm=0.001, frame=north)['edit']\n" +
                "assert all(close(a, b) for a, b in zip(edit['vector'], (0, -0.25, 0))), edit\n" +
                "assert sn.plan(state, 'in', per_mm=0.001)['edit']['vector'][2] == -0.25\n" +
                "assert sn.plan(state, 'out', per_mm=0.001)['edit']['vector'][2] == 0.25\n" +
                "r = sn.plan({'target': 2, 'step_mm': 100.0}, 'left', per_mm=0.001)\n" +
                "assert r['edit'] is None and r['problem'], r");
        }

        [Fact]
        public void ScreenMoves_UseTheCamerasOwnAxes_WhenAsked()
        {
            Run("import math\n" +
                "s, c = math.sin(math.radians(31.5)), math.cos(math.radians(31.5))\n" +
                "tilted = (math.sin(math.radians(15.75)), 0.0, 0.0, math.cos(math.radians(15.75)))\n" +   // 31.5 deg about X
                "raw = sn.frame_for(tilted, snap=False)\n" +
                "assert close(raw['forward'][1], s) and close(raw['forward'][2], -c), raw\n" +
                "assert close(raw['up'][1], c) and close(raw['up'][2], s), raw\n" +
                "assert all(close(a, b) for a, b in zip(raw['right'], (1.0, 0.0, 0.0))), raw\n" +
                "assert sn.frame_for(tilted) == sn.frame_for(None), sn.frame_for(tilted)\n" +   // still mostly looking down

                "state = sn.plan({'target': 'box', 'step_mm': 100.0}, 'moves:screen')['state']\n" +
                "assert state['moves'] == 'screen'\n" +
                "edit = sn.plan(state, 'up', per_mm=1.0, frame=raw)['edit']\n" +
                "assert close(edit['vector'][1], 100 * c) and close(edit['vector'][2], 100 * s), edit\n" +
                "assert sn.plan(state, 'moves:sideways')['problem']\n" +
                "assert sn.toast_for(state, 'moves:screen', None, 'Meters', 0.001) == 'Box moves along the screen'\n" +
                "back = sn.plan(state, 'moves:world')['state']\n" +
                "assert sn.toast_for(back, 'moves:world', None, 'Meters', 0.001) == 'Box moves along the world axes'");
        }

        [Fact]
        public void Frame_SnapsTheCameraToWorldAxes_LikeTheViewCube()
        {
            Run("f = sn.frame_for(None)\n" +
                "assert f == {'right': (1.0, 0.0, 0.0), 'up': (0.0, 1.0, 0.0), 'forward': (0.0, 0.0, -1.0)}, f\n" +
                "f = sn.frame_for((0.7071067811865476, 0.0, 0.0, 0.7071067811865476))\n" +   // looking north, Z up
                "assert f == {'right': (1.0, 0.0, 0.0), 'up': (0.0, 0.0, 1.0), 'forward': (0.0, 1.0, 0.0)}, f\n" +
                "f = sn.frame_from_vectors((0.3, 0.8, -0.5), (0.1, 0.4, 0.9))\n" +           // a tilted 3D view
                "assert f == {'right': (1.0, 0.0, 0.0), 'up': (0.0, 0.0, 1.0), 'forward': (0.0, 1.0, 0.0)}, f\n" +
                "f = sn.frame_from_vectors((0.0, 0.0, 0.0), (0.0, 0.0, 0.0))\n" +           // degenerate: plan view
                "assert f == sn.frame_for(None), f");
        }

        [Fact]
        public void FaceNames_ComeFromTheFrame_AndTiltedPlanesAreNamedSo()
        {
            Run("plan = sn.frame_for(None)\n" +
                "assert sn.face_names(plan) == {'Front': (0.0, 0.0, 1.0), 'Back': (0.0, 0.0, -1.0),\n" +
                "                               'Right': (1.0, 0.0, 0.0), 'Left': (-1.0, 0.0, 0.0),\n" +
                "                               'Top': (0.0, 1.0, 0.0), 'Bottom': (0.0, -1.0, 0.0)}\n" +
                "assert sn.name_for_normal((0.0, 0.0, -1.0), plan) == 'Front'\n" +           // inward -Z: the +Z face
                "assert sn.name_for_normal((1.0, 0.0, 0.0), plan) == 'Left'\n" +
                "assert sn.name_for_normal((0.7, 0.7, 0.0), plan) is None\n" +
                "assert sn.axis_label((0.0, 0.0, 1.0)) == '+Z' and sn.axis_label((-1.0, 0.0, 0.0)) == '-X'\n" +
                "assert sn.axis_label((0.7, 0.7, 0.0)) == 'tilted'");
        }

        [Fact]
        public void Layout_PutsEachPlaneInItsCell_AndTheRestUnderOthers()
        {
            Run("plan = sn.frame_for(None)\n" +
                "cells = sn.layout(snapshot(mode='box'), plan)\n" +
                "assert cells['cells'] == {'Right': 0, 'Left': 1, 'Top': 2, 'Bottom': 3, 'Front': 4, 'Back': 5}, cells\n" +
                "assert cells['others'] == []\n" +
                "cells = sn.layout(snapshot(mode='planes'), plan)\n" +          // six planes all with normal +X
                "assert cells['cells'] == {'Right': None, 'Left': 0, 'Top': None, 'Bottom': None, 'Front': None, 'Back': None}, cells\n" +
                "assert cells['others'] == [1, 2, 3, 4, 5], cells\n" +
                "snap = snapshot(mode='planes')\n" +
                "snap['planes'][1]['normal'] = (0.6, 0.6, 0.5)\n" +
                "cells = sn.layout(snap, plan)\n" +
                "assert cells['others'] == [1, 2, 3, 4, 5]\n" +
                "assert cells['labels'][1] == 'Plane 2, tilted', cells['labels']\n" +
                "assert cells['labels'][0] == 'Plane 1, Left (-X)', cells['labels']\n" +
                "assert cells['labels'][4] == 'Plane 5, off', cells['labels']\n" +
                "assert sn.layout(snapshot(enabled=False, mode='planes'), plan)['cells']['Left'] == 0\n" +
                "assert sn.layout(None, plan)['cells'] == dict.fromkeys(sn.CELLS)");
        }

        [Fact]
        public void Presets_FollowTheDocumentUnits()
        {
            Run("metric = sn.presets('Millimeters')\n" +
                "assert [label for label, mm in metric] == ['10 mm', '50 mm', '100 mm', '500 mm', '1 m'], metric\n" +
                "assert [mm for label, mm in metric] == [10.0, 50.0, 100.0, 500.0, 1000.0]\n" +
                "assert sn.presets('Meters') == metric\n" +
                "imperial = sn.presets('Feet')\n" +
                "assert [label for label, mm in imperial] == ['1/4in', '1in', '6in', '1ft', '5ft'], imperial\n" +
                "assert close(imperial[3][1], 304.8)");
        }

        [Fact]
        public void Keys_MapToActions_ByTarget()
        {
            Run("assert sn.action_for_key('Up', 3) == 'in'\n" +
                "assert sn.action_for_key('Down', 3) == 'out'\n" +
                "assert sn.action_for_key('Up', 'box') == 'up'\n" +
                "assert sn.action_for_key('Down', 'box') == 'down'\n" +
                "assert sn.action_for_key('Left', 'box') == 'left'\n" +
                "assert sn.action_for_key('Right', 'box') == 'right'\n" +
                "assert sn.action_for_key('PageUp', 'box') == 'nearer'\n" +
                "assert sn.action_for_key('Next', 'box') == 'farther'\n" +       // WPF's name for Page Down
                "assert sn.action_for_key('Left', 3) == 'prev'\n" +
                "assert sn.action_for_key('Right', 3) == 'next'\n" +
                "assert sn.action_for_key('Tab', 3) == 'next'\n" +
                "assert sn.action_for_key('Add', 3) == 'double'\n" +
                "assert sn.action_for_key('OemPlus', 3) == 'double'\n" +
                "assert sn.action_for_key('Subtract', 3) == 'halve'\n" +
                "assert sn.action_for_key('OemMinus', 3) == 'halve'\n" +
                "assert sn.action_for_key('D3', 3) == 'target:2'\n" +
                "assert sn.action_for_key('NumPad6', 3) == 'target:5'\n" +
                "assert sn.action_for_key('D0', 3) == 'target:box'\n" +
                "assert sn.action_for_key('D7', 3) is None\n" +
                "assert sn.action_for_key('F5', 3) is None");
        }

        [Fact]
        public void Describe_ReadsTheTargetOffTheSnapshot()
        {
            Run("snap = snapshot()\n" +
                "plan = sn.frame_for(None)\n" +
                "text = sn.describe({'target': 2, 'step_mm': 100.0}, snap, 'Meters', per_mm=0.001, frame=plan)\n" +
                "assert text['target'] == 'Plane 3, Left (-X)', text\n" +
                "text = sn.describe({'target': 2, 'step_mm': 100.0}, snapshot(mode='box'), 'Meters', per_mm=0.001, frame=plan)\n" +
                "assert text['target'] == 'Top face (+Y)', text\n" +
                "text = sn.describe({'target': 2, 'step_mm': 100.0}, snap, 'Meters', per_mm=0.001)\n" +
                "assert text['target'] == 'Plane 3, Left (-X)', text\n" +
                "assert text['readout'] == 'On, normal +X, 20.000 m along it', text\n" +
                "assert text['step'] == '0.100 m', text\n" +
                "assert sn.describe({'target': 5, 'step_mm': 100.0}, snap, 'Meters', per_mm=0.001)['readout'].startswith('Off,')\n" +
                "text = sn.describe({'target': 'box', 'step_mm': 100.0}, snap, 'Meters', per_mm=0.001)\n" +
                "assert text['target'] == 'Box', text\n" +
                "assert text['readout'] == 'Centre 2.000, 1.000, 0.500 m; size 4.000 x 2.000 x 1.000 m', text\n" +
                "assert sn.describe({'target': 'box'}, snapshot(enabled=False), 'Meters', 0.001)['readout'] == 'Sectioning is off'\n" +
                "assert sn.describe({'target': 'box'}, None, 'Meters', 0.001)['readout'] == 'No document'\n" +
                "assert sn.describe({'target': 0, 'step_mm': 304.8}, snap, 'Feet', per_mm=1.0 / 304.8)['step'] == chr(49) + chr(39) + ' 0' + chr(34)");
        }

        [Fact]
        public void ImperialLengths_ReadAndWrite_TheWayRevitDoes()
        {
            Run("ft, inch = chr(39), chr(34)\n" +
                "assert sn.format_length(0.5, 'Feet') == '0' + ft + ' 6' + inch\n" +
                "assert sn.format_length(1.0 / 96, 'Feet') == '0' + ft + ' 0 1/8' + inch\n" +
                "assert sn.format_length(18.5, 'Inches') == '1' + ft + ' 6 1/2' + inch\n" +
                "assert sn.format_length(-0.25, 'Feet') == '-0' + ft + ' 3' + inch\n" +
                "p = lambda t, u='Feet': sn.parse_length(t, u)\n" +
                "assert close(p('0 0 1/8'), 1.0 / 96)\n" +                       // feet, inches, fraction
                "assert close(p('0.5'), 0.5)\n" +                               // a bare number is feet
                "assert close(p('0 6'), 0.5)\n" +                               // feet then inches
                "assert close(p('1 6 1/2'), 1.5 + 0.5 / 12)\n" +
                "assert close(p('1' + ft + ' 6' + inch), 1.5)\n" +
                "assert close(p('1' + ft + '6' + inch), 1.5)\n" +
                "assert close(p('6 1/2' + inch), 6.5 / 12)\n" +
                "assert close(p('18' + inch), 1.5)\n" +
                "assert close(p('1-6'), 1.5)\n" +
                "assert close(p('1/8'), 1.0 / 96)\n" +                          // a bare fraction is inches
                "assert close(p('2 ft 3 in'), 2.25)\n" +
                "assert close(p('6', 'Inches'), 6.0)\n" +                        // an inch document: bare number is inches
                "assert close(p('1' + ft, 'Inches'), 12.0)\n" +
                "assert close(p('0.5', 'Meters'), 0.5)\n" +
                "assert p('', 'Feet') is None and p('abc', 'Feet') is None and p('1 x', 'Meters') is None\n" +
                "assert p('1/0', 'Feet') is None and p(ft, 'Feet') is None");
        }

        [Fact]
        public void ToastLines_ForTheChordBundles()
        {
            Run("state = {'target': 2, 'step_mm': 100.0}\n" +
                "edit = {'kind': 'plane', 'index': 2, 'distance': 0.1}\n" +
                "assert sn.toast_for(state, 'in', edit, 'Meters', 0.001) == 'Plane 3 in by 0.100 m'\n" +
                "edit = {'kind': 'box', 'vector': (0, 0, -0.1)}\n" +
                "assert sn.toast_for({'target': 'box', 'step_mm': 100.0}, 'in', edit, 'Meters', 0.001) == 'Box down by 0.100 m'\n" +
                "assert sn.toast_for({'target': 'box', 'step_mm': 100.0}, 'left', edit, 'Meters', 0.001) == 'Box left by 0.100 m'\n" +
                "assert sn.toast_for({'target': 'box', 'step_mm': 100.0}, 'nearer', edit, 'Meters', 0.001) == 'Box nearer by 0.100 m'\n" +
                "assert sn.toast_for(state, 'next', None, 'Meters', 0.001) == 'Target: Plane 3'\n" +
                "assert sn.toast_for(state, 'double', None, 'Meters', 0.001) == 'Step: 0.100 m'");
        }
    }
}
