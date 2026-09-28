using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The Excel Sets bundle's pure row&lt;-&gt;spec converters
    /// (rows_to_spec / spec_to_rows), imported straight out of the shipped
    /// script.py through the real IronPython engine.
    ///
    /// script.py doubles as an importable module: the actual click behaviour
    /// (run_export/run_import/forms.ask_options) sits behind
    /// "if '__commandpath__' in globals(): run()" at the bottom of the file,
    /// and plain "import script" gives the import its own fresh module
    /// namespace with no '__commandpath__' in it, so importing never opens a
    /// dialog or touches Navisworks - only the two pure functions run.
    /// </summary>
    public class SetsFromExcelScriptTests
    {
        private readonly IronPythonEngine _engine;

        public SetsFromExcelScriptTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(BundleDir());
            _engine.Initialize(config);
        }

        private static string BundleDir()
        {
            var candidate = Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension",
                "pyNavis.tab", "06_Data.panel", "03_Excel_Sets.pushbutton"));
            if (!Directory.Exists(candidate))
                throw new DirectoryNotFoundException("Excel_Sets.pushbutton not found at " + candidate);
            return candidate;
        }

        private ExecResult Run(string code, TextWriter output = null)
        {
            return _engine.Execute(new ScriptRequest { Code = "import script\n" + code, Output = output });
        }

        [Fact]
        public void SpecToRows_OneRowPerCondition_RepeatsNameAndFolderForMultiConditionSets()
        {
            var outw = new StringWriter();
            var r = Run(
                "spec = {'sets': [\n" +
                "    {'name': 'Walls', 'folder': None, 'query': {'or': [{'and': [\n" +
                "        {'category': 'Item', 'prop': 'Type', 'op': 'equals', 'value': 'Wall'}]}]}},\n" +
                "    {'name': 'Tall Steel', 'folder': 'Structure/Steel', 'query': {'or': [{'and': [\n" +
                "        {'category': 'Item', 'prop': 'Material', 'op': 'equals', 'value': 'Steel'},\n" +
                "        {'category': 'Item', 'prop': 'Height', 'op': 'gt', 'value': 3.0}]}]}},\n" +
                "]}\n" +
                "rows = script.spec_to_rows(spec)\n" +
                "assert rows == [\n" +
                "    ['', 'Walls', 'Item', 'Type', 'equals', 'Wall'],\n" +
                "    ['Structure/Steel', 'Tall Steel', 'Item', 'Material', 'equals', 'Steel'],\n" +
                "    ['Structure/Steel', 'Tall Steel', 'Item', 'Height', 'gt', 3.0],\n" +
                "], rows\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void SpecToRows_OpsWithoutPropOrValue_LeaveThoseColumnsBlank()
        {
            var outw = new StringWriter();
            var r = Run(
                "spec = {'sets': [{'name': 'Ductwork', 'folder': None, 'query': {'or': [{'and': [\n" +
                "    {'category': 'Item', 'op': 'has_category'},\n" +
                "    {'category': 'Item', 'prop': 'Fire Rating', 'op': 'has_property'},\n" +
                "]}]}}]}\n" +
                "rows = script.spec_to_rows(spec)\n" +
                "assert rows == [\n" +
                "    ['', 'Ductwork', 'Item', '', 'has_category', ''],\n" +
                "    ['', 'Ductwork', 'Item', 'Fire Rating', 'has_property', ''],\n" +
                "], rows\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void SpecToRows_SetWithNoConditions_StillGetsOneBlankRow()
        {
            var outw = new StringWriter();
            var r = Run(
                "empty_query = {'name': 'Empty', 'folder': None, 'query': {'or': [{'and': []}]}}\n" +
                "static_set = {'name': 'Static', 'folder': 'Old', 'items': 5}\n" +
                "rows = script.spec_to_rows({'sets': [empty_query, static_set]})\n" +
                "assert rows == [\n" +
                "    ['', 'Empty', '', '', '', ''],\n" +
                "    ['Old', 'Static', '', '', '', ''],\n" +
                "], rows\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void SpecToRows_RawCondition_SurfacesItsTextInTheValueColumn()
        {
            var outw = new StringWriter();
            var r = Run(
                "spec = {'sets': [{'name': 'Weird', 'folder': None, 'query': {'or': [{'and': [\n" +
                "    {'op': 'raw', 'text': 'SomeUnmappedComparison'}]}]}}]}\n" +
                "rows = script.spec_to_rows(spec)\n" +
                "assert rows == [['', 'Weird', '', '', 'raw', 'SomeUnmappedComparison']], rows\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void RowsToSpec_GroupsConsecutiveRowsByFolderAndName_IntoOneAndGroup()
        {
            var outw = new StringWriter();
            var r = Run(
                "rows = [\n" +
                "    ['', 'Walls', 'Item', 'Type', 'equals', 'Wall'],\n" +
                "    ['Structure/Steel', 'Tall Steel', 'Item', 'Material', 'equals', 'Steel'],\n" +
                "    ['Structure/Steel', 'Tall Steel', 'Item', 'Height', 'gt', 3.0],\n" +
                "]\n" +
                "spec, skipped = script.rows_to_spec(rows)\n" +
                "assert skipped == 0, skipped\n" +
                "assert spec == {'sets': [\n" +
                "    {'name': 'Walls', 'folder': None, 'query': {'or': [{'and': [\n" +
                "        {'category': 'Item', 'prop': 'Type', 'op': 'equals', 'value': 'Wall'}]}]}},\n" +
                "    {'name': 'Tall Steel', 'folder': 'Structure/Steel', 'query': {'or': [{'and': [\n" +
                "        {'category': 'Item', 'prop': 'Material', 'op': 'equals', 'value': 'Steel'},\n" +
                "        {'category': 'Item', 'prop': 'Height', 'op': 'gt', 'value': 3.0}]}]}},\n" +
                "]}, spec\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void RowsToSpec_BlankOpRows_AreSkippedEntirely_AndCounted()
        {
            // A blank-op block carries no condition at all. Emitting it as an empty
            // AND group would be a MATCH-EVERYTHING search set, which
            // sets._validate_import now rejects - and one rejected entry aborts the
            // WHOLE import. Those rows are the static/explicit sets export_all(
            // include_static=True) puts in the workbook for visibility, so the
            // import side drops them and reports the count instead.
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import sets\n" +
                "rows = [\n" +
                "    ['', 'Empty', '', '', '', ''],\n" +
                "    ['Old', 'Static', '', '', '', ''],\n" +
                "    ['', 'Walls', 'Item', 'Type', 'equals', 'Wall'],\n" +
                "]\n" +
                "spec, skipped = script.rows_to_spec(rows)\n" +
                "assert skipped == 2, skipped\n" +
                "assert spec == {'sets': [\n" +
                "    {'name': 'Walls', 'folder': None, 'query': {'or': [{'and': [\n" +
                "        {'category': 'Item', 'prop': 'Type', 'op': 'equals', 'value': 'Wall'}]}]}}]}, spec\n" +
                "# what the skipping is FOR: the surviving spec passes validation, so the\n" +
                "# blank rows can never abort an import that had real sets in it\n" +
                "assert sets._validate_import(spec) == [], sets._validate_import(spec)\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void RowsToSpec_AllRowsBlank_YieldsNoSetsAtAll()
        {
            var outw = new StringWriter();
            var r = Run(
                "spec, skipped = script.rows_to_spec([['', 'Empty', '', '', '', '']])\n" +
                "assert spec == {'sets': []}, spec\n" +
                "assert skipped == 1, skipped\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void RowsToSpec_RawOp_RebuildsTheRawMarkerFromTheValueColumn()
        {
            var outw = new StringWriter();
            var r = Run(
                "rows = [['', 'Weird', '', '', 'raw', 'SomeUnmappedComparison']]\n" +
                "spec, skipped = script.rows_to_spec(rows)\n" +
                "assert skipped == 0, skipped\n" +
                "assert spec == {'sets': [{'name': 'Weird', 'folder': None, 'query': {'or': [{'and': [\n" +
                "    {'op': 'raw', 'text': 'SomeUnmappedComparison'}]}]}}]}, spec\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void RowsToSpec_TwoSetsWithTheSameNameInDifferentBlocks_StayAsTwoSets()
        {
            // Not adjacent, so this is two separate blocks, exactly like two
            // distinct exported sets that happen to share a display name -
            // grouping never merges non-adjacent rows.
            var outw = new StringWriter();
            var r = Run(
                "rows = [\n" +
                "    ['', 'Dup', 'Item', 'Type', 'equals', 'A'],\n" +
                "    ['', 'Other', 'Item', 'Type', 'equals', 'B'],\n" +
                "    ['', 'Dup', 'Item', 'Type', 'equals', 'C'],\n" +
                "]\n" +
                "spec, skipped = script.rows_to_spec(rows)\n" +
                "assert skipped == 0, skipped\n" +
                "assert len(spec['sets']) == 3, spec\n" +
                "assert [e['name'] for e in spec['sets']] == ['Dup', 'Other', 'Dup']\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void RoundTrip_SpecToRowsToSpec_IsLossless_ForPortableQueries()
        {
            // Lossless for every entry that CAN be imported. A conditionless entry
            // (an empty AND group, or the {'items': N} static set export_all(
            // include_static=True) emits) is deliberately NOT round-tripped: it goes
            // out as a blank row for visibility and is skipped on the way back.
            var outw = new StringWriter();
            var r = Run(
                "portable = [\n" +
                "    {'name': 'Walls', 'folder': None, 'query': {'or': [{'and': [\n" +
                "        {'category': 'Item', 'prop': 'Type', 'op': 'equals', 'value': 'Wall'}]}]}},\n" +
                "    {'name': 'Tall Steel', 'folder': 'Structure/Steel', 'query': {'or': [{'and': [\n" +
                "        {'category': 'Item', 'prop': 'Material', 'op': 'equals', 'value': 'Steel'},\n" +
                "        {'category': 'Item', 'prop': 'Height', 'op': 'gt', 'value': 3.0}]}]}},\n" +
                "    {'name': 'Ductwork', 'folder': None, 'query': {'or': [{'and': [\n" +
                "        {'category': 'Item', 'op': 'has_category'}]}]}},\n" +
                "]\n" +
                "spec = {'sets': portable + [\n" +
                "    {'name': 'Empty', 'folder': None, 'query': {'or': [{'and': []}]}},\n" +
                "    {'name': 'Static', 'folder': 'Old', 'items': 5},\n" +
                "]}\n" +
                "back, skipped = script.rows_to_spec(script.spec_to_rows(spec))\n" +
                "assert back == {'sets': portable}, back\n" +
                "assert skipped == 2, skipped\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void RoundTrip_RowsToSpecToRows_IsLossless_MinusTheSkippedBlankRows()
        {
            var outw = new StringWriter();
            var r = Run(
                "rows = [\n" +
                "    ['', 'Walls', 'Item', 'Type', 'equals', 'Wall'],\n" +
                "    ['Structure/Steel', 'Tall Steel', 'Item', 'Material', 'equals', 'Steel'],\n" +
                "    ['Structure/Steel', 'Tall Steel', 'Item', 'Height', 'gt', 3.0],\n" +
                "    ['', 'Empty', '', '', '', ''],\n" +
                "]\n" +
                "spec, skipped = script.rows_to_spec(rows)\n" +
                "assert skipped == 1, skipped\n" +
                "assert script.spec_to_rows(spec) == rows[:3]\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void EmptyInput_RoundTripsToEmpty()
        {
            var outw = new StringWriter();
            var r = Run(
                "assert script.spec_to_rows({'sets': []}) == []\n" +
                "assert script.rows_to_spec([]) == ({'sets': []}, 0)\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void RealExcelRoundTrip_IntegralValueComesBackAsInt_DecimalStaysFloat()
        {
            // The pure-converter round trips above never touch pynavis.xl, so they
            // never saw this: xl.read hands EVERY numeric cell back as float (see
            // XlModule_WritesAndReadsXlsx_RoundTrip in PyNavisLibTests.cs - 42 comes
            // back as 42.0), so an exported int condition value used to re-import as
            // a float, changing what it compiles to (FromDouble instead of
            // FromInt32) and how it stringifies for contains/wildcard ('42.0' instead
            // of '42'). rows_to_spec now normalizes an integral float back to int;
            // a genuine decimal (1.5) is left alone. This goes through the REAL
            // xl.write + xl.read, not just the two pure functions against each
            // other, which is what let the bug through the first time.
            var r = Run(
                "import tempfile, os\n" +
                "spec = {'sets': [{'name': 'Mixed', 'folder': None, 'query': {'or': [{'and': [\n" +
                "    {'category': 'Item', 'prop': 'Count', 'op': 'equals', 'value': 42},\n" +
                "    {'category': 'Item', 'prop': 'Height', 'op': 'gt', 'value': 1.5},\n" +
                "]}]}}]}\n" +
                "rows = script.spec_to_rows(spec)\n" +
                "path = os.path.join(tempfile.mkdtemp(), 'sets_from_excel.xlsx')\n" +
                "script.xl.write(path, rows, headers=script.HEADERS)\n" +
                "raw_rows = script.xl.read(path)\n" +
                "data_rows = raw_rows[1:] if raw_rows and raw_rows[0] == script.HEADERS else raw_rows\n" +
                "back, skipped = script.rows_to_spec(data_rows)\n" +
                "assert skipped == 0, skipped\n" +
                "conditions = back['sets'][0]['query']['or'][0]['and']\n" +
                "by_prop = dict((c['prop'], c['value']) for c in conditions)\n" +
                "\n" +
                "count_value = by_prop['Count']\n" +
                "assert isinstance(count_value, int) and not isinstance(count_value, bool), (type(count_value), count_value)\n" +
                "assert count_value == 42, count_value\n" +
                "\n" +
                "height_value = by_prop['Height']\n" +
                "assert isinstance(height_value, float), (type(height_value), height_value)\n" +
                "assert height_value == 1.5, height_value\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ImportingScript_NeverCallsRun_NoCommandpathMeansNoDialog()
        {
            // If the guard at the bottom of script.py were missing or wrong,
            // "import script" would call forms.ask_options and this test
            // would hang waiting on a modal dialog instead of returning.
            var outw = new StringWriter();
            var r = Run("print('imported without running')", outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("imported without running", outw.ToString());
        }
    }
}
