using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure half of Hide Tabs (extensions/pyNavis.extension/lib/tabhider.py):
    /// which tabs may be taken off the ribbon, which of the chosen ones are there
    /// to take, where each goes back, and the words. The ribbon calls themselves
    /// need a live Navisworks and are field-checked; field-measured, Navisworks
    /// resets every add-in tab's IsVisible on each idle, so tabs are taken out of
    /// the ribbon's list rather than hidden.
    /// </summary>
    public class TabHiderTests
    {
        private readonly IronPythonEngine _engine;

        public TabHiderTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension", "lib")));
            _engine.Initialize(config);
        }

        // The ribbon as the field probe listed it, trimmed, plus a contextual
        // tab and a second pyNavis tab from someone's own extension.
        private const string Ribbon =
            "import tabhider as th\n" +
            "def tab(id, title, contextual=False):\n" +
            "    return {'id': id, 'title': title, 'contextual': contextual}\n" +
            "tabs = [tab('ID_TabHome', 'Home'),\n" +
            "        tab('ID_RibbonTab_ItemTools', 'Item Tools'),\n" +
            "        tab('ID_RibbonTab_SectioningTools', 'Sectioning Tools'),\n" +
            "        tab('RibbonTab_RecordingTab', 'Recording'),\n" +
            "        tab('NavisworksGLTFPlugin.ADSK.ID_GLTF', 'ProtoTech GLTF'),\n" +
            "        tab('Some.Contextual', 'Something', contextual=True),\n" +
            "        tab('ViewpointsImagesExporter.Codigem. Codigem ', ' Codigem '),\n" +
            "        tab('PYNAVIS_TAB_pyNavis_pyNavis', 'pyNavis'),\n" +
            "        tab('PYNAVIS_TAB_Mine_Tools', 'Tools')]\n" +
            "GLTF, CODIGEM = 'NavisworksGLTFPlugin.ADSK.ID_GLTF', 'ViewpointsImagesExporter.Codigem. Codigem '\n" +
            "def apply(current, plan):\n" +
            "    current = list(current)\n" +
            "    for id, index in plan:\n" +
            "        current.insert(index, id)\n" +
            "    return current\n";

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = Ribbon + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void Offerable_LeavesOutTabsNavisworksOpensOnDemand_AndPyNavisTabs()
        {
            // Item Tools, Sectioning Tools and Recording open when they are
            // needed; taking them out would stop that. pyNavis's own tabs hold
            // the button that brings the rest back.
            Run("ids = [t['id'] for t in th.offerable(tabs)]\n" +
                "assert ids == ['ID_TabHome', GLTF, CODIGEM], ids");
        }

        [Fact]
        public void ToHide_IsTheChosenTabsThatArePresentAndOfferable_InRibbonOrder()
        {
            Run("chosen = [CODIGEM, 'ID_RibbonTab_ItemTools', 'Uninstalled.Addin.Tab',\n" +
                "          'PYNAVIS_TAB_pyNavis_pyNavis', 'ID_TabHome']\n" +
                "assert th.to_hide(tabs, chosen) == ['ID_TabHome', CODIGEM], th.to_hide(tabs, chosen)\n" +
                "assert th.to_hide(tabs, []) == []");
        }

        [Fact]
        public void RestorePlan_PutsEachTabBackAfterTheTabThatPrecededIt()
        {
            Run("original = ['A', 'B', 'C', 'D', 'E']\n" +
                "plan = th.restore_plan(original, ['B', 'D'], ['A', 'C', 'E'])\n" +
                "assert plan == [('B', 1), ('D', 3)], plan\n" +
                "assert apply(['A', 'C', 'E'], plan) == original\n" +
                // a tab that arrived while the others were out stays where it is
                "assert apply(['A', 'C', 'E', 'X'], th.restore_plan(original, ['B', 'D'], ['A', 'C', 'E', 'X'])) \\\n" +
                "    == ['A', 'B', 'C', 'D', 'E', 'X']\n" +
                // the first tab goes back first
                "assert th.restore_plan(['A', 'B', 'C'], ['A'], ['B', 'C']) == [('A', 0)]");
        }

        [Fact]
        public void RestorePlan_CopesWithNeighboursThatMovedOrLeft()
        {
            // Reload rebuilds the pyNavis tab (P) at the end of the ribbon, and a
            // neighbour can be gone altogether: each tab still finds its place.
            Run("plan = th.restore_plan(['A', 'B', 'P', 'C'], ['B'], ['A', 'C', 'P'])\n" +
                "assert apply(['A', 'C', 'P'], plan) == ['A', 'B', 'C', 'P'], plan\n" +
                "plan = th.restore_plan(['A', 'B', 'C', 'D', 'E'], ['B', 'D'], ['C', 'E'])\n" +
                "assert apply(['C', 'E'], plan) == ['B', 'C', 'D', 'E'], plan");
        }

        [Fact]
        public void Words_NameTheTabs()
        {
            // The picker opens with last time's choice ticked, so its prompt no
            // longer has to name it.
            Run("assert th.title_of(tabs[6]) == 'Codigem'\n" +
                "assert th.hidden_message(['NavisLookup', 'VREX']) == ('2 tabs hidden', 'NavisLookup, VREX. Click again to bring them back.')\n" +
                "assert th.hidden_message(['VREX']) == ('1 tab hidden', 'VREX. Click again to bring it back.')\n" +
                "assert th.shown_message(3) == '3 tabs are back'\n" +
                "assert th.shown_message(1) == '1 tab is back'\n" +
                "assert th.PROMPT == 'Tick the tabs to take off the ribbon.'\n" +
                "assert not hasattr(th, 'prompt_for')\n" +
                "assert th.TOOL == 'hide_tabs' and th.DEFAULTS == {'tabs': []}");
        }
    }
}
