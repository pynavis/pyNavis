using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The Select by IDs bundle's pure search planning: which condition each
    /// strategy compiles to, and how the winning strategy is promoted so a
    /// whole paste costs one search per id. Imported straight out of the
    /// shipped script.py, which only runs its click behaviour behind
    /// "if '__commandpath__' in globals()" (see SetsFromExcelScriptTests).
    /// </summary>
    public class SelectByIdsScriptTests
    {
        private readonly IronPythonEngine _engine;

        public SelectByIdsScriptTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(Dir("lib"));
            config.SearchPaths.Add(Dir(Path.Combine(
                "pyNavis.tab", "06_Data.panel", "02_Element_IDs.stack", "01_Select_by_IDs.pushbutton")));
            _engine.Initialize(config);
        }

        private static string Dir(string relative)
        {
            var candidate = Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension", relative));
            if (!Directory.Exists(candidate))
                throw new DirectoryNotFoundException("not found at " + candidate);
            return candidate;
        }

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "import script\n" +
                       "VALUES = {'id_category': 'Element ID', 'id_property': 'Value'}\n" +
                       code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void Strategies_TryTheExporterTabFirst_AndTheNameSuffixLast()
        {
            // The NWC property tab is the common case, so it is tried first;
            // the name suffix is the design coordination fallback.
            //
            // Text leads number even though both read that tab: the Revit
            // exporter stores the id as a DisplayString, so the numeric
            // comparison matches nothing on a real export (field-checked),
            // and a strategy that misses costs a whole walk of
            // the model. Number stays for a model that does store it as an
            // Int32, just not in front.
            Run("assert script.STRATEGIES == ('text', 'number', 'name'), script.STRATEGIES");
        }

        [Fact]
        public void ConditionFor_Number_ComparesTheConfiguredPropertyToAnInt()
        {
            Run("c = script.condition_for('number', '123456', VALUES)\n" +
                "assert c == {'category': 'Element ID', 'prop': 'Value',\n" +
                "             'op': 'equals', 'value': 123456}, c\n" +
                "assert isinstance(c['value'], int), type(c['value'])");
        }

        [Fact]
        public void ConditionFor_Text_ComparesTheSamePropertyToAString()
        {
            // Which variant kind the exporter wrote is not knowable up front,
            // and an int/string mismatch matches nothing silently, so both
            // comparisons exist.
            Run("c = script.condition_for('text', '123456', VALUES)\n" +
                "assert c == {'category': 'Element ID', 'prop': 'Value',\n" +
                "             'op': 'equals', 'value': '123456'}, c");
        }

        [Fact]
        public void StrategiesFor_DropsThePropertyShapeTheModelDoesNotStore()
        {
            // Every strategy tried on an id that misses costs a full walk of
            // the model, so the shape the model provably does not store is
            // not free to keep. The name suffix always stays: it answers a
            // different question. Unknown keeps everything in play.
            Run("assert script.strategies_for('text') == ('text', 'name')\n" +
                "assert script.strategies_for('number') == ('number', 'name')\n" +
                "assert script.strategies_for(None) == script.STRATEGIES");
        }

        [Fact]
        public void KindOf_ReadsTheVariantFlags_TextFirst()
        {
            // The same Is* probes pynavis.props uses; anything but the two
            // shapes a search condition can be built for is None.
            Run("class V(object):\n" +
                "    def __init__(self, **flags):\n" +
                "        self.__dict__.update(flags)\n" +
                "assert script.kind_of(V(IsDisplayString=True)) == 'text'\n" +
                "assert script.kind_of(V(IsInt32=True)) == 'number'\n" +
                "assert script.kind_of(V(IsDisplayString=True, IsInt32=True)) == 'text'\n" +
                "assert script.kind_of(V(IsDouble=True)) is None\n" +
                "assert script.kind_of(V()) is None");
        }

        [Fact]
        public void QueryFor_RequiresARealElement_WhenTheModelMarksThem()
        {
            // The bug this pins: a family instance carries "Element ID" = its
            // own id, but so does every nested part under it, holding the
            // TYPE's id (Revit Type > Id). Without the category check a pasted
            // type id resolved to "the first instance of that family". By
            // internal name, so it survives a non-English Navisworks.
            Run("q = script.query_for('text', '12345678', VALUES, True)\n" +
                "conds = q['or'][0]['and']\n" +
                "assert len(conds) == 2, conds\n" +
                "assert conds[0] == {'category': 'Element ID', 'prop': 'Value',\n" +
                "                    'op': 'equals', 'value': '12345678'}, conds[0]\n" +
                "assert conds[1] == {'category': 'LcRevitData_Element',\n" +
                "                    'op': 'has_category', 'by_display': False}, conds[1]\n" +
                "q = script.query_for('number', '12345678', VALUES, True)\n" +
                "assert len(q['or'][0]['and']) == 2, q");
        }

        [Fact]
        public void QueryFor_NeverRequiresTheCategory_ForTheNameSuffix()
        {
            // A design coordination model has no Revit categories at all;
            // there the bracketed name suffix IS the element.
            Run("q = script.query_for('name', '12345678', VALUES, True)\n" +
                "conds = q['or'][0]['and']\n" +
                "assert len(conds) == 1, conds\n" +
                "assert conds[0]['op'] == 'contains' and conds[0]['value'] == '[12345678]'");
        }

        [Fact]
        public void QueryFor_IsTheBareCondition_WhenTheModelDoesNotMarkElements()
        {
            // An exporter that never wrote the category must not turn every
            // id into a miss; and a strategy that cannot apply stays None.
            Run("q = script.query_for('text', '12345678', VALUES, False)\n" +
                "assert q == {'or': [{'and': [script.condition_for('text', '12345678', VALUES)]}]}, q\n" +
                "assert script.query_for('number', '2147483648', VALUES, True) is None");
        }

        [Fact]
        public void ConditionFor_Number_AboveInt32_IsSkippedSoTextCanHandleIt()
        {
            // VariantData.FromInt32 is what a python int compiles to; a larger
            // id has to be matched as text instead of overflowing.
            Run("assert script.condition_for('number', '2147483647', VALUES) is not None\n" +
                "assert script.condition_for('number', '2147483648', VALUES) is None\n" +
                "assert script.condition_for('text', '2147483648', VALUES) is not None");
        }

        [Fact]
        public void ConditionFor_Name_MatchesBothBracketsSoAShorterIdCannotMatchALongerOne()
        {
            Run("c = script.condition_for('name', '123456', VALUES)\n" +
                "assert c == {'category': 'Item', 'prop': 'Name',\n" +
                "             'op': 'contains', 'value': '[123456]'}, c");
        }

        [Fact]
        public void ConditionFor_Name_IgnoresTheConfiguredPropertyNames()
        {
            // The name suffix convention is fixed; only the property tab is
            // configurable.
            Run("other = {'id_category': 'Revit ID', 'id_property': 'Id'}\n" +
                "assert script.condition_for('name', '77', other) == \\\n" +
                "       script.condition_for('name', '77', VALUES)");
        }

        [Fact]
        public void Promote_MovesTheWinningStrategyToTheFront()
        {
            Run("assert script.promote(['number', 'text', 'name'], 'name') == \\\n" +
                "       ['name', 'number', 'text']\n" +
                "assert script.promote(['number', 'text', 'name'], 'number') == \\\n" +
                "       ['number', 'text', 'name']");
        }

        [Fact]
        public void Promote_UnknownStrategy_LeavesTheOrderAlone()
        {
            Run("assert script.promote(['number', 'text'], 'nope') == ['number', 'text']");
        }

        [Fact]
        public void ShortList_TruncatesLongIdListsForTheToastDetail()
        {
            Run("assert script.short_list(['1', '2', '3']) == '1, 2, 3'\n" +
                "many = [str(n) for n in range(1, 12)]\n" +
                "assert script.short_list(many) == '1, 2, 3, 4, 5, 6, 7, 8 and 3 more', \\\n" +
                "       script.short_list(many)");
        }

        [Fact]
        public void ClipboardPrefill_FillsTheBox_ButOnlyFromTextThatLooksLikeIds()
        {
            // The clipboard fills the box in; it never starts a search by
            // itself. Prose that merely contains a number has to come back
            // empty, or the box would open holding a list nobody meant.
            Run("import script as s\n" +
                "s.script.clipboard_text = lambda: '11101054, 1168175'\n" +
                "assert s.clipboard_prefill({'use_clipboard': True}) == \\\n" +
                "       '11101054, 1168175'\n" +
                "s.script.clipboard_text = lambda: 'Basic Wall [123456]'\n" +
                "assert s.clipboard_prefill({'use_clipboard': True}) == '123456'\n" +
                "s.script.clipboard_text = lambda: 'see section 12 for details'\n" +
                "assert s.clipboard_prefill({'use_clipboard': True}) == ''\n" +
                "s.script.clipboard_text = lambda: ''\n" +
                "assert s.clipboard_prefill({'use_clipboard': True}) == ''");
        }

        [Fact]
        public void ClipboardPrefill_IsEmpty_WhenTheSettingIsOff()
        {
            // And the setting is checked before the clipboard is read at all,
            // so turning it off costs nothing and leaks nothing.
            Run("import script as s\n" +
                "def boom():\n" +
                "    raise AssertionError('clipboard read while the setting was off')\n" +
                "s.script.clipboard_text = boom\n" +
                "assert s.clipboard_prefill({'use_clipboard': False}) == ''");
        }
    }
}
