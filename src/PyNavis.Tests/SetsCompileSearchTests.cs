using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// sets.compile_search's wiring of the Search object itself, as opposed to
    /// the conditions inside it. Both facts pinned here were field-measured in
    /// Navisworks and cost a live debugging session: a fresh
    /// Api.Search() is scoped to NOTHING and defaults PruneBelowMatch to True,
    /// so a search that is merely "compiled correctly" still matches zero items
    /// in the whole model.
    ///
    /// Run against a python stub standing in for the Navisworks API, injected
    /// into sys.modules as pynavis._api before compile_search's lazy import
    /// reaches for it - the same trick that lets the API half of any pynavis
    /// module be tested outside a live host.
    /// </summary>
    public class SetsCompileSearchTests
    {
        private readonly IronPythonEngine _engine;

        public SetsCompileSearchTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            _engine.Initialize(config);
        }

        /// <summary>
        /// The stub API: a Search that records every SelectAll() and every write
        /// to PruneBelowMatch, so a test can tell "left at the default" apart
        /// from "explicitly set to the same value".
        /// </summary>
        private const string FakeApi =
            "import sys, types\n" +
            "class FakeSelection(object):\n" +
            "    def __init__(self):\n" +
            "        self.select_all_calls = 0\n" +
            "        self.scoped_to = None\n" +
            "    def SelectAll(self):\n" +
            "        self.select_all_calls += 1\n" +
            "    def CopyFrom(self, items):\n" +
            "        self.scoped_to = list(items)\n" +
            "class FakeConditions(object):\n" +
            "    def __init__(self):\n" +
            "        self.added = []\n" +
            "    def Add(self, condition):\n" +
            "        self.added.append(condition)\n" +
            "class FakeItemCollection(object):\n" +
            "    def __init__(self):\n" +
            "        self.items = []\n" +
            "    def Add(self, item):\n" +
            "        self.items.append(item)\n" +
            "    def __iter__(self):\n" +
            "        return iter(self.items)\n" +
            "    @property\n" +
            "    def Count(self):\n" +
            "        return len(self.items)\n" +
            "FIRSTS = []\n" +          // what each FindFirst call hands back, in order
            "FIRST_CALLS = []\n" +
            "class FakeSearch(object):\n" +
            "    def __init__(self):\n" +
            "        self.Selection = FakeSelection()\n" +
            "        self.SearchConditions = FakeConditions()\n" +
            "        self.prune_writes = []\n" +
            "        object.__setattr__(self, '_PruneBelowMatch', True)\n" +
            "    def __setattr__(self, name, value):\n" +
            "        if name == 'PruneBelowMatch':\n" +
            "            self.prune_writes.append(value)\n" +
            "            object.__setattr__(self, '_PruneBelowMatch', value)\n" +
            "            return\n" +
            "        object.__setattr__(self, name, value)\n" +
            "    @property\n" +
            "    def PruneBelowMatch(self):\n" +
            "        return object.__getattribute__(self, '_PruneBelowMatch')\n" +
            "    def FindFirst(self, document, report):\n" +
            "        FIRST_CALLS.append(self)\n" +
            "        return FIRSTS.pop(0) if FIRSTS else None\n" +
            "class FakeCondition(object):\n" +
            "    def __init__(self, kind, *args):\n" +
            "        self.kind = kind\n" +
            "        self.args = args\n" +
            "        self.value = None\n" +
            "    def EqualValue(self, variant):\n" +
            "        self.value = variant\n" +
            "        return self\n" +
            "class FakeSearchCondition(object):\n" +
            "    @staticmethod\n" +
            "    def HasPropertyByDisplayName(category, prop):\n" +
            "        return FakeCondition('prop_display', category, prop)\n" +
            "    @staticmethod\n" +
            "    def HasPropertyByName(category, prop):\n" +
            "        return FakeCondition('prop_name', category, prop)\n" +
            "class FakeVariantData(object):\n" +
            "    @staticmethod\n" +
            "    def FromDisplayString(text):\n" +
            "        return ('display', text)\n" +
            "    @staticmethod\n" +
            "    def FromInt32(number):\n" +
            "        return ('int', number)\n" +
            "class FakeApi(object):\n" +
            "    Search = FakeSearch\n" +
            "    SearchCondition = FakeSearchCondition\n" +
            "    VariantData = FakeVariantData\n" +
            "    ModelItemCollection = FakeItemCollection\n" +
            "module = types.ModuleType('pynavis._api')\n" +
            "module.Api = FakeApi\n" +
            // pynavis.doc imports Application by name at module level; find_first
            // reaches it through "from pynavis import doc", so the stub has to
            // carry it even though every test here passes an explicit document.
            "module.Application = None\n" +
            "sys.modules['pynavis._api'] = module\n" +
            "import pynavis\n" +
            "pynavis._api = module\n" +
            "from pynavis import sets\n";

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = FakeApi + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void EverySearch_IsScopedToTheWholeModel()
        {
            // The bug this pins: without SelectAll() the Search is scoped to an
            // empty selection and FindAll returns 0 for every predicate, so
            // Select by IDs reported "No elements matched" for ids that were
            // sitting on the selected item's own property tab.
            Run(
                "q = {'or': [{'and': [{'category': 'Element ID', 'prop': 'Value',\n" +
                "                      'op': 'equals', 'value': '11101054'}]}]}\n" +
                "searches = sets.compile_search(q)\n" +
                "assert len(searches) == 1, len(searches)\n" +
                "assert searches[0].Selection.select_all_calls == 1, \\\n" +
                "    searches[0].Selection.select_all_calls\n");
        }

        [Fact]
        public void EveryOrGroupsSearch_IsScopedToTheWholeModel()
        {
            // One Search per OR group, and the scope has to be set on each of
            // them: run() unions their FindAll results, so one unscoped search
            // silently contributes nothing to the union.
            Run(
                "q = {'or': [\n" +
                "    {'and': [{'category': 'Item', 'prop': 'Name',\n" +
                "              'op': 'equals', 'value': 'A'}]},\n" +
                "    {'and': [{'category': 'Item', 'prop': 'Name',\n" +
                "              'op': 'equals', 'value': 'B'}]},\n" +
                "]}\n" +
                "searches = sets.compile_search(q)\n" +
                "assert len(searches) == 2, len(searches)\n" +
                "for search in searches:\n" +
                "    assert search.Selection.select_all_calls == 1, \\\n" +
                "        search.Selection.select_all_calls\n");
        }

        [Fact]
        public void PruneBelowMatch_IsWrittenExplicitly_NotLeftAtTheApiDefault()
        {
            // Api.Search() defaults PruneBelowMatch to True, so an unpruned
            // query only stays unpruned because compile_search writes the False
            // back. Dropping that assignment as "redundant" would silently prune
            // every search in the library.
            Run(
                "q = {'or': [{'and': [{'category': 'Item', 'prop': 'Name',\n" +
                "                      'op': 'equals', 'value': 'A'}]}]}\n" +
                "search = sets.compile_search(q)[0]\n" +
                "assert search.prune_writes == [False], search.prune_writes\n" +
                "assert search.PruneBelowMatch is False, search.PruneBelowMatch\n");
        }

        [Fact]
        public void AScopedSearch_ReplacesSelectAll_WithTheGivenItems()
        {
            // The escape hatch from "every search walks the whole model":
            // scoping to an item confines the walk to that item's own branch,
            // which is how Select by IDs sweeps up the rest of a multi-part
            // element for free after FindFirst located one of its nodes.
            Run(
                "q = {'or': [{'and': [{'category': 'Item', 'prop': 'Name',\n" +
                "                      'op': 'equals', 'value': 'A'}]}]}\n" +
                "search = sets.compile_search(q, None, ['ITEM1', 'ITEM2'])[0]\n" +
                "assert search.Selection.select_all_calls == 0, \\\n" +
                "    search.Selection.select_all_calls\n" +
                "assert search.Selection.scoped_to == ['ITEM1', 'ITEM2'], \\\n" +
                "    search.Selection.scoped_to\n");
        }

        [Fact]
        public void FindFirst_StopsAtTheFirstGroupThatHits()
        {
            // FindFirst exists to stop the walk early, so it must also stop
            // trying OR groups the moment one of them produces an item -
            // running the rest would give back the whole cost it just saved.
            Run(
                "del FIRSTS[:]\n" +
                "del FIRST_CALLS[:]\n" +
                "FIRSTS.extend(['HIT', 'SECOND'])\n" +
                "q = {'or': [\n" +
                "    {'and': [{'category': 'Item', 'prop': 'Name',\n" +
                "              'op': 'equals', 'value': 'A'}]},\n" +
                "    {'and': [{'category': 'Item', 'prop': 'Name',\n" +
                "              'op': 'equals', 'value': 'B'}]},\n" +
                "]}\n" +
                "assert sets.find_first(q, 'DOC') == 'HIT'\n" +
                "assert len(FIRST_CALLS) == 1, len(FIRST_CALLS)\n" +
                "assert FIRST_CALLS[0].Selection.select_all_calls == 1\n");
        }

        [Fact]
        public void FindFirst_ReturnsNone_WhenNoGroupMatches()
        {
            // None, not an empty collection: the caller's "did this id resolve"
            // test is an identity check, and every group still gets its turn.
            Run(
                "del FIRSTS[:]\n" +
                "del FIRST_CALLS[:]\n" +
                "q = {'or': [\n" +
                "    {'and': [{'category': 'Item', 'prop': 'Name',\n" +
                "              'op': 'equals', 'value': 'A'}]},\n" +
                "    {'and': [{'category': 'Item', 'prop': 'Name',\n" +
                "              'op': 'equals', 'value': 'B'}]},\n" +
                "]}\n" +
                "assert sets.find_first(q, 'DOC') is None\n" +
                "assert len(FIRST_CALLS) == 2, len(FIRST_CALLS)\n");
        }
    }
}
