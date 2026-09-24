using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure half of the Select by IDs / IDs of Selection bundles:
    /// extensions/pyNavis.extension/lib/elementids.py, imported through the
    /// real IronPython engine. Nothing in that module touches Navisworks, so
    /// importing it here runs no host code and opens no dialog.
    /// </summary>
    public class ElementIdsTests
    {
        private readonly IronPythonEngine _engine;

        public ElementIdsTests()
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
                Code = "import elementids\n" + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        // ---- parse_ids: the dialog's permissive tokenizer ------------------

        [Fact]
        public void ParseIds_CommaSeparated_ReturnsIdsInOrder()
        {
            Run("ids, rejected = elementids.parse_ids('123456, 234567, 345678')\n" +
                "assert ids == ['123456', '234567', '345678'], ids\n" +
                "assert rejected == [], rejected");
        }

        [Fact]
        public void ParseIds_MixedSeparators_SplitsOnNewlinesTabsAndSemicolons()
        {
            Run("ids, rejected = elementids.parse_ids('111\\n222\\t333;444  555')\n" +
                "assert ids == ['111', '222', '333', '444', '555'], ids\n" +
                "assert rejected == [], rejected");
        }

        [Fact]
        public void ParseIds_BracketedToken_StripsTheBrackets()
        {
            Run("ids, rejected = elementids.parse_ids('[123456] (234567)')\n" +
                "assert ids == ['123456', '234567'], ids\n" +
                "assert rejected == [], rejected");
        }

        [Fact]
        public void ParseIds_RepeatedId_IsKeptOnceInFirstSeenOrder()
        {
            Run("ids, rejected = elementids.parse_ids('222, 111, 222, 111')\n" +
                "assert ids == ['222', '111'], ids");
        }

        [Fact]
        public void ParseIds_LeadingZeros_AreStrippedSoTheyMatchTheStoredId()
        {
            Run("ids, rejected = elementids.parse_ids('000123456')\n" +
                "assert ids == ['123456'], ids");
        }

        [Fact]
        public void ParseIds_NonNumericToken_IsReportedAsRejected()
        {
            Run("ids, rejected = elementids.parse_ids('123456 Wall 234567')\n" +
                "assert ids == ['123456', '234567'], ids\n" +
                "assert rejected == ['Wall'], rejected");
        }

        [Fact]
        public void ParseIds_EmptyText_ReturnsTwoEmptyLists()
        {
            Run("assert elementids.parse_ids('   \\n  ') == ([], []), elementids.parse_ids('   ')\n" +
                "assert elementids.parse_ids(None) == ([], [])");
        }

        // ---- bracket_ids: the design-coordination naming convention --------

        [Fact]
        public void BracketIds_FindsEveryBracketedIdInOrder()
        {
            Run("text = 'Basic Wall [123456]\\nRound Duct [234567]'\n" +
                "assert elementids.bracket_ids(text) == ['123456', '234567'], elementids.bracket_ids(text)");
        }

        [Fact]
        public void BracketIds_IgnoresBracketsThatAreNotAllDigits()
        {
            Run("assert elementids.bracket_ids('Wall [Type A] [123456]') == ['123456']");
        }

        [Fact]
        public void BracketIds_RepeatedId_IsKeptOnce()
        {
            Run("assert elementids.bracket_ids('A [77] B [77]') == ['77']");
        }

        // ---- clipboard_ids: what may auto-run without the user asking ------

        [Fact]
        public void ClipboardIds_CleanListOfNumbers_IsAccepted()
        {
            Run("assert elementids.clipboard_ids('123456, 234567') == ['123456', '234567']");
        }

        [Fact]
        public void ClipboardIds_NavisworksItemNames_AreAcceptedViaTheirBrackets()
        {
            Run("text = 'Basic Wall [123456]\\nRound Duct [234567]'\n" +
                "assert elementids.clipboard_ids(text) == ['123456', '234567']");
        }

        [Fact]
        public void ClipboardIds_ProseThatMerelyContainsANumber_IsRejected()
        {
            Run("assert elementids.clipboard_ids('Lets meet in 2024 about the wall') == []");
        }

        [Fact]
        public void ClipboardIds_HugeText_IsRejectedWithoutScanning()
        {
            Run("text = '123456, ' * 40000\n" +
                "assert len(text) > elementids.MAX_CLIPBOARD_CHARS\n" +
                "assert elementids.clipboard_ids(text) == []");
        }

        [Fact]
        public void ClipboardIds_EmptyOrNone_IsRejected()
        {
            Run("assert elementids.clipboard_ids('') == []\n" +
                "assert elementids.clipboard_ids(None) == []");
        }

        // ---- the settings both bundles share -------------------------------

        [Fact]
        public void Defaults_CarryEveryKeyBothBundlesRead_UnderOneSharedStoreKey()
        {
            // Both bundles read this one store, so the key set is a contract
            // between them and the settings dialog, not an implementation
            // detail of either script.
            Run("d = elementids.DEFAULTS\n" +
                "assert sorted(d) == ['every_match', 'id_category', 'id_property', " +
                "'isolate', 'use_clipboard', 'zoom'], sorted(d)\n" +
                "assert d['id_category'] == 'Element ID', d['id_category']\n" +
                "assert d['id_property'] == 'Value', d['id_property']\n" +
                "assert d['zoom'] is True and d['use_clipboard'] is True\n" +
                "assert d['isolate'] is False\n" +
                // Off by default: one id selects one item. A federated model
                // measured 1, 35, 1, 5 and 16 carriers for five ids, so
                // selecting every copy is the mode you ask for, not the one
                // you get for typing an id.
                "assert d['every_match'] is False\n" +
                "assert elementids.SETTINGS_KEY");
        }

        [Fact]
        public void IsElement_LooksForTheExporterCategory_ByInternalName()
        {
            // (display, name) pairs as pynavis.props.categories() hands them
            // over. Internal name only: the display name is localised.
            Run("assert elementids.ELEMENT_CATEGORY == 'LcRevitData_Element'\n" +
                "assert elementids.is_element([('Item', 'LcOaNode'),\n" +
                "                              ('Element', 'LcRevitData_Element'),\n" +
                "                              ('Element ID', 'LcRevitId')])\n" +
                "assert not elementids.is_element([('Item', 'LcOaNode'),\n" +
                "                                  ('Element ID', 'LcRevitId')])\n" +
                "assert not elementids.is_element([('Element', 'SomethingElse')])\n" +
                "assert not elementids.is_element([])");
        }

        // ---- id_from_name: reading an id back off a selected item ----------

        [Fact]
        public void IdFromName_TrailingBracket_IsTheId()
        {
            Run("assert elementids.id_from_name('Basic Wall [123456]') == '123456'");
        }

        [Fact]
        public void IdFromName_SeveralBrackets_TakesTheLastAllDigitsOne()
        {
            Run("assert elementids.id_from_name('Wall [Type A] [123456]') == '123456'");
        }

        [Fact]
        public void IdFromName_NoBracketedNumber_IsNone()
        {
            Run("assert elementids.id_from_name('Basic Wall') is None\n" +
                "assert elementids.id_from_name('Wall [Type A]') is None\n" +
                "assert elementids.id_from_name(None) is None");
        }

        // ---- format_id_list: what lands on the clipboard --------------------

        [Fact]
        public void FormatIdList_JoinsWithCommaAndSpace()
        {
            Run("assert elementids.format_id_list(['1', '2', '3']) == '1, 2, 3'\n" +
                "assert elementids.format_id_list([]) == ''");
        }
    }
}
