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
            Run("assert sn.normalize(None) == {'target': 'box', 'step_mm': 100.0}\n" +
                "assert sn.normalize({'target': 9, 'step_mm': -5}) == {'target': 'box', 'step_mm': 100.0}\n" +
                "assert sn.normalize({'target': 3, 'step_mm': 25}) == {'target': 3, 'step_mm': 25.0}\n" +
                "assert sn.normalize({'target': '2'}) == {'target': 2, 'step_mm': 100.0}");
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
                "r = sn.plan(state, 'x+', per_mm=0.001)\n" +
                "assert r['edit'] is None and r['problem'], r");
        }

        [Fact]
        public void BoxMoves_AreWorldVectors_AndInOutMeanDownAndUp()
        {
            Run("state = {'target': 'box', 'step_mm': 250.0}\n" +
                "for action, vector in (('x+', (0.25, 0, 0)), ('x-', (-0.25, 0, 0)),\n" +
                "                       ('y+', (0, 0.25, 0)), ('y-', (0, -0.25, 0)),\n" +
                "                       ('z+', (0, 0, 0.25)), ('z-', (0, 0, -0.25))):\n" +
                "    edit = sn.plan(state, action, per_mm=0.001)['edit']\n" +
                "    assert edit['kind'] == 'box'\n" +
                "    assert all(close(a, b) for a, b in zip(edit['vector'], vector)), (action, edit)\n" +
                "assert sn.plan(state, 'in', per_mm=0.001)['edit']['vector'][2] == -0.25\n" +
                "assert sn.plan(state, 'out', per_mm=0.001)['edit']['vector'][2] == 0.25");
        }

        [Fact]
        public void Keys_MapToActions_ByTarget()
        {
            Run("assert sn.action_for_key('Up', 3) == 'in'\n" +
                "assert sn.action_for_key('Down', 3) == 'out'\n" +
                "assert sn.action_for_key('Up', 'box') == 'y+'\n" +
                "assert sn.action_for_key('Down', 'box') == 'y-'\n" +
                "assert sn.action_for_key('Left', 'box') == 'x-'\n" +
                "assert sn.action_for_key('Right', 'box') == 'x+'\n" +
                "assert sn.action_for_key('PageUp', 'box') == 'z+'\n" +
                "assert sn.action_for_key('Next', 'box') == 'z-'\n" +       // WPF's name for Page Down
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
                "text = sn.describe({'target': 2, 'step_mm': 100.0}, snap, 'Meters', per_mm=0.001)\n" +
                "assert text['target'] == 'Plane 3', text\n" +
                "assert text['readout'] == 'On, normal +X, 20.000 m along it', text\n" +
                "assert text['step'] == '0.100 m', text\n" +
                "assert sn.describe({'target': 5, 'step_mm': 100.0}, snap, 'Meters', per_mm=0.001)['readout'].startswith('Off,')\n" +
                "text = sn.describe({'target': 'box', 'step_mm': 100.0}, snap, 'Meters', per_mm=0.001)\n" +
                "assert text['target'] == 'Box', text\n" +
                "assert text['readout'] == 'Centre 2.000, 1.000, 0.500 m; size 4.000 x 2.000 x 1.000 m', text\n" +
                "assert sn.describe({'target': 'box'}, snapshot(enabled=False), 'Meters', 0.001)['readout'] == 'Sectioning is off'\n" +
                "assert sn.describe({'target': 'box'}, None, 'Meters', 0.001)['readout'] == 'No document'\n" +
                "assert sn.describe({'target': 0, 'step_mm': 304.8}, snap, 'Feet', per_mm=1.0 / 304.8)['step'] == '1ft 0in'");
        }

        [Fact]
        public void ToastLines_ForTheChordBundles()
        {
            Run("state = {'target': 2, 'step_mm': 100.0}\n" +
                "edit = {'kind': 'plane', 'index': 2, 'distance': 0.1}\n" +
                "assert sn.toast_for(state, 'in', edit, 'Meters', 0.001) == 'Plane 3 in by 0.100 m'\n" +
                "edit = {'kind': 'box', 'vector': (0, 0, -0.1)}\n" +
                "assert sn.toast_for({'target': 'box', 'step_mm': 100.0}, 'in', edit, 'Meters', 0.001) == 'Box down by 0.100 m'\n" +
                "assert sn.toast_for(state, 'next', None, 'Meters', 0.001) == 'Target: Plane 3'\n" +
                "assert sn.toast_for(state, 'double', None, 'Meters', 0.001) == 'Step: 0.100 m'");
        }
    }
}
