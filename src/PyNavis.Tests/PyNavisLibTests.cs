using System;
using System.IO;
using System.Linq;
using PyNavis.Runtime.Engine;
using Execution = PyNavis.Runtime.Execution;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Tests for the pynavislib python package, executed through the real IronPython
    /// engine. Navisworks-API-bound modules can only be exercised inside Navisworks;
    /// here we pin down what CAN run anywhere: package import, version, pure helpers,
    /// and that every shipped module at least compiles.
    /// </summary>
    public class PyNavisLibTests
    {
        private readonly IronPythonEngine _engine;

        /// <summary>Repo pynavislib folder, found by walking up from the test binaries.</summary>
        public static string PyNavisLibDir { get; } = FindPyNavisLib();

        public PyNavisLibTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibDir);
            _engine.Initialize(config);
        }

        private static string FindPyNavisLib()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "pynavislib");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException(
                "pynavislib not found above " + AppDomain.CurrentDomain.BaseDirectory);
        }

        private ExecResult Run(string code, TextWriter output = null)
        {
            return _engine.Execute(new ScriptRequest { Code = code, Output = output });
        }

        [Fact]
        public void ImportPyNavis_Works_WithoutNavisworksApi()
        {
            var outw = new StringWriter();
            var r = Run("import pynavis\nprint('version=' + pynavis.__version__)", outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("version=1.", outw.ToString());
        }

        [Fact]
        public void ScriptModule_ExposesCommandContext_FromHost()
        {
            Execution.PyNavisHost.Instance.SetCommandContext(
                @"C:\bundles\Tool.pushbutton\script.py", @"C:\bundles\Tool.pushbutton", "My Tool");

            var outw = new StringWriter();
            var r = Run(
                "from pynavis import script\n" +
                "print('path=' + script.get_script_path())\n" +
                "print('dir=' + script.get_command_path())\n" +
                "print('title=' + script.get_title())\n" +
                "print('host=' + script.get_host_version())",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            var text = outw.ToString();
            Assert.Contains(@"path=C:\bundles\Tool.pushbutton\script.py", text);
            Assert.Contains(@"dir=C:\bundles\Tool.pushbutton", text);
            Assert.Contains("title=My Tool", text);
            Assert.Contains("host=", text);
        }

        // Shared fixture for the pure viewpoint rename engine: a folder with two
        // views, plus a top-level animation, in tree order.
        private const string VpRows =
            "from pynavis import viewpoints\n" +
            "rows = [\n" +
            " {'key':'0','guid':'g-site','parent_key':'','name':'Site','depth':0,'is_folder':True,'kind':'folder'},\n" +
            " {'key':'0/0','guid':'g-north','parent_key':'0','name':'North View','depth':1,'is_folder':False,'kind':'viewpoint'},\n" +
            " {'key':'0/1','guid':'g-south','parent_key':'0','name':'South View','depth':1,'is_folder':False,'kind':'viewpoint'},\n" +
            " {'key':'1','guid':'g-walk','parent_key':'','name':'Walkthrough','depth':0,'is_folder':False,'kind':'animation'},\n" +
            "]\n" +
            "all_keys = ['0', '0/0', '0/1', '1']\n";

        [Fact]
        public void OutputModule_TableHtml_MarksNumericColumnsByTheirData()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import output\n" +
                "rows = [['Level 01', 412, 'Open'], ['Level 02', 96, 'Closed']]\n" +
                "html = output.table_html(rows, ['Area', 'Clashes', 'Status'])\n" +
                "print('numcells=%d' % html.count('<td class=\"num\">'))\n" +
                "print('numheads=%d' % html.count('<th class=\"num\">'))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            // only the Clashes column: alignment follows the data, not the
            // column's position in the table
            Assert.Contains("numcells=2", outw.ToString());
            Assert.Contains("numheads=1", outw.ToString());
        }

        [Fact]
        public void ViewpointsModule_Replace_RenamesCheckedOnly_AndOmitsUnchanged()
        {
            var outw = new StringWriter();
            var r = Run(VpRows +
                "p = viewpoints.plan_renames(rows, ['0/0'], {'type':'replace','find':'View','replace':'Cam'})\n" +
                "print('renames=%s' % [(x['key'], x['new']) for x in p['renames']])\n" +
                "q = viewpoints.plan_renames(rows, all_keys, {'type':'replace','find':'zzz','replace':'y'})\n" +
                "print('noop=%d' % len(q['renames']))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("renames=[('0/0', 'North Cam')]", outw.ToString());
            Assert.Contains("noop=0", outw.ToString());
        }

        [Fact]
        public void ViewpointsModule_Regex_Substitutes_AndReportsBadPatterns()
        {
            var outw = new StringWriter();
            var r = Run(VpRows +
                "p = viewpoints.plan_renames(rows, ['0/0','0/1'],\n" +
                "    {'type':'replace','find':'(\\\\w+) View','replace':'\\\\1','regex':True})\n" +
                "print('renames=%s' % [x['new'] for x in p['renames']])\n" +
                "bad = viewpoints.plan_renames(rows, ['0/0'], {'type':'replace','find':'(','regex':True})\n" +
                "print('bad=%d renames=%d' % (len(bad['problems']), len(bad['renames'])))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("renames=['North', 'South']", outw.ToString());
            Assert.Contains("bad=1 renames=0", outw.ToString());
        }

        [Fact]
        public void ViewpointsModule_Affix_And_Case_Apply()
        {
            var outw = new StringWriter();
            var r = Run(VpRows +
                "p = viewpoints.plan_renames(rows, ['1'], {'type':'affix','prefix':'VP - ','suffix':' (ok)'})\n" +
                "print('affix=%s' % p['renames'][0]['new'])\n" +
                "c = viewpoints.plan_renames(rows, ['1'], {'type':'case','mode':'upper'})\n" +
                "print('upper=%s' % c['renames'][0]['new'])\n" +
                "t = viewpoints.plan_renames(rows, ['1'], {'type':'case','mode':'lower'})\n" +
                "print('lower=%s' % t['renames'][0]['new'])",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("affix=VP - Walkthrough (ok)", outw.ToString());
            Assert.Contains("upper=WALKTHROUGH", outw.ToString());
            Assert.Contains("lower=walkthrough", outw.ToString());
        }

        [Fact]
        public void ViewpointsModule_Numbering_SkipsFolders_InTreeOrder()
        {
            var outw = new StringWriter();
            var r = Run(VpRows +
                "p = viewpoints.plan_renames(rows, all_keys,\n" +
                "    {'type':'number','pattern':'View {n}','start':5,'pad':2})\n" +
                "print('renames=%s' % [(x['key'], x['new']) for x in p['renames']])",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains(
                "renames=[('0/0', 'View 05'), ('0/1', 'View 06'), ('1', 'View 07')]",
                outw.ToString());
        }

        [Fact]
        public void ViewpointsModule_Collisions_BlockDuplicateSiblings_AndEmptyNames()
        {
            var outw = new StringWriter();
            var r = Run(VpRows +
                // Planned vs planned: numbering both children to the same fixed name.
                "p = viewpoints.plan_renames(rows, ['0/0','0/1'], {'type':'number','pattern':'Same','start':1,'pad':0})\n" +
                "print('both=%d' % len(p['collisions']))\n" +
                "print('flags=%s' % [x['collision'] for x in p['renames']])\n" +
                // Planned vs unchecked existing sibling.
                "q = viewpoints.plan_renames(rows, ['0/0'], {'type':'replace','find':'North','replace':'South'})\n" +
                "print('existing=%d' % len(q['collisions']))\n" +
                // Whole name replaced away -> empty.
                "e = viewpoints.plan_renames(rows, ['1'], {'type':'replace','find':'Walkthrough','replace':''})\n" +
                "print('empty=%d' % len(e['collisions']))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("both=2", outw.ToString());
            Assert.Contains("flags=[True, True]", outw.ToString());
            Assert.Contains("existing=1", outw.ToString());
            Assert.Contains("empty=1", outw.ToString());
        }

        [Fact]
        public void ViewpointsModule_Deletes_CollapseFullyCheckedFolders()
        {
            var outw = new StringWriter();
            var r = Run(VpRows +
                // Folder 0 and both children checked: the folder covers them.
                "p = viewpoints.plan_deletes(rows, ['0', '0/0', '0/1'])\n" +
                "print('full=%s count=%d' % (p['keys'], p['count']))\n" +
                // Folder checked but one child unchecked: folder survives.
                "q = viewpoints.plan_deletes(rows, ['0', '0/0'])\n" +
                "print('partial=%s count=%d' % (q['keys'], q['count']))\n" +
                // Child alone inside an unchecked folder deletes individually.
                "s = viewpoints.plan_deletes(rows, ['0/1', '1'])\n" +
                "print('leaves=%s count=%d' % (s['keys'], s['count']))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("full=['0'] count=3", outw.ToString());
            Assert.Contains("partial=['0/0'] count=1", outw.ToString());
            Assert.Contains("leaves=['0/1', '1'] count=2", outw.ToString());
        }

        [Fact]
        public void ViewpointsModule_PlanEntries_CarryGuids_ForSafeApply()
        {
            var outw = new StringWriter();
            var r = Run(VpRows +
                "p = viewpoints.plan_renames(rows, ['0/0'], {'type':'affix','prefix':'X '})\n" +
                "print('rename=%s' % [(x['guid'], x['new']) for x in p['renames']])\n" +
                "d = viewpoints.plan_deletes(rows, ['0', '0/0', '0/1'])\n" +
                "print('delkeys=%s guids=%s count=%d' % (d['keys'], d['guids'], d['count']))\n" +
                "print('lookup=%s' % viewpoints.guids_for(rows, ['1', 'nope', '0/1']))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("rename=[('g-north', 'X North View')]", outw.ToString());
            Assert.Contains("delkeys=['0'] guids=['g-site'] count=3", outw.ToString());
            Assert.Contains("lookup=['g-south', 'g-walk']", outw.ToString());
        }

        [Fact]
        public void ViewpointsModule_DeleteOrder_IsDescending_AndNumeric()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import viewpoints\n" +
                "print(viewpoints.delete_order(['0', '0/2', '0/10', '1']))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("['1', '0/10', '0/2', '0']", outw.ToString());
        }

        [Fact]
        public void ViewpointsModule_SortOps_ProduceFoldersFirstAlphabetical()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import viewpoints\n" +
                "def replay(entries, ops):\n" +
                "    sim = list(entries)\n" +
                "    for f, t in ops:\n" +
                "        sim.insert(t, sim.pop(f))\n" +
                "    return [name for name, _ in sim]\n" +
                "plain = [('b', False), ('A', False), ('c', False)]\n" +
                "print('plain=%s' % replay(plain, viewpoints.sort_ops(plain)))\n" +
                "mixed = [('z view', False), ('Alpha', True)]\n" +
                "print('mixed=%s' % replay(mixed, viewpoints.sort_ops(mixed)))\n" +
                "done = [('A', True), ('a view', False), ('b view', False)]\n" +
                "print('noop=%d' % len(viewpoints.sort_ops(done)))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("plain=['A', 'b', 'c']", outw.ToString());
            Assert.Contains("mixed=['Alpha', 'z view']", outw.ToString());
            Assert.Contains("noop=0", outw.ToString());
        }

        [Fact]
        public void ViewpointsModule_ValidateMove_RejectsFolderIntoItsOwnSubtree()
        {
            var outw = new StringWriter();
            var r = Run(VpRows +
                "print('self=%s' % (viewpoints.validate_move(rows, ['0'], '0') is not None))\n" +
                "rows2 = rows + [{'key':'0/2','parent_key':'0','name':'Inner','depth':1,'is_folder':True,'kind':'folder'}]\n" +
                "print('child=%s' % (viewpoints.validate_move(rows2, ['0'], '0/2') is not None))\n" +
                "print('ok=%s' % (viewpoints.validate_move(rows, ['1'], '0') is None))\n" +
                "print('root=%s' % (viewpoints.validate_move(rows, ['0/0'], '') is None))\n" +
                "print('notfolder=%s' % (viewpoints.validate_move(rows, ['1'], '0/0') is not None))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("self=True", outw.ToString());
            Assert.Contains("child=True", outw.ToString());
            Assert.Contains("ok=True", outw.ToString());
            Assert.Contains("root=True", outw.ToString());
            Assert.Contains("notfolder=True", outw.ToString());
        }

        [Fact]
        public void SettingsModule_Merge_KeepsOnlyDefaultKeys_AndPrefersStored()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import settings\n" +
                "d = {'smart': True, 'tolerance_m': 2.0}\n" +
                "m = settings.merge(d, {'smart': False, 'junk': 1})\n" +
                "print('smart=%s tol=%s junk=%s' % (m['smart'], m['tolerance_m'], 'junk' in m))\n" +
                "print('fresh=%s' % (settings.merge(d, {}) is not d))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("smart=False tol=2.0 junk=False", outw.ToString());
            Assert.Contains("fresh=True", outw.ToString());
        }

        [Fact]
        public void SettingsModule_SaveLoad_RoundTrips_UnderOverrideRoot()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import settings\n" +
                $"settings._APPDATA_OVERRIDE = r'{tmp}'\n" +
                "settings.save('t1', {'a': 1, 'b': 'x'})\n" +
                "m = settings.load('t1', {'a': 0, 'b': '', 'c': True})\n" +
                "print('a=%s b=%s c=%s' % (m['a'], m['b'], m['c']))\n" +
                "m2 = settings.load('missing', {'a': 7})\n" +
                "print('missing=%s' % m2['a'])\n" +
                "print('path_ok=%s' % settings.path_for('t1').endswith('settings\\\\t1.json'))",
                outw);

            try { Directory.Delete(tmp, true); } catch { }
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("a=1 b=x c=True", outw.ToString());
            Assert.Contains("missing=7", outw.ToString());
            Assert.Contains("path_ok=True", outw.ToString());
        }

        [Fact]
        public void SettingsModule_CorruptFile_FallsBackToDefaults()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tmp, "pyNavis", "settings"));
            File.WriteAllText(Path.Combine(tmp, "pyNavis", "settings", "bad.json"), "{ not json");

            var outw = new StringWriter();
            var r = Run(
                "from pynavis import settings\n" +
                $"settings._APPDATA_OVERRIDE = r'{tmp}'\n" +
                "m = settings.load('bad', {'a': 5})\n" +
                "print('a=%s' % m['a'])",
                outw);

            try { Directory.Delete(tmp, true); } catch { }
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("a=5", outw.ToString());
        }

        [Fact]
        public void OutputModule_FormatTable_AlignsColumns_AndStringifiesCells()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import output\n" +
                "rows = [['Test A', 3, 'New'], ['B', 12, 'Active']]\n" +
                "print(output.format_table(rows, ['Name', 'Count', 'Status']))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            var lines = outw.ToString().Replace("\r", "").Split('\n');
            Assert.Equal("Name    Count  Status", lines[0]);
            Assert.Equal("------  -----  ------", lines[1]);
            Assert.Equal("Test A  3      New", lines[2]);
            Assert.Equal("B       12     Active", lines[3]);
        }

        [Fact]
        public void OutputModule_FormatTable_EmptyRows_YieldsHeaderOnly()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import output\n" +
                "print(output.format_table([], ['Name']))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            var lines = outw.ToString().Replace("\r", "").Split('\n');
            Assert.Equal("Name", lines[0]);
            Assert.Equal("----", lines[1]);
        }

        [Fact]
        public void UtilFlatten_YieldsGroupLikeLeaves_InsteadOfDescendingIntoThem()
        {
            // Regression: ClashTest derives from GroupItem, so a leaf-check that ran
            // AFTER the group-check descended into tests and yielded their results.
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import _util\n" +
                "class Node(object):\n" +
                "    def __init__(self, name, kind, *children):\n" +
                "        self.name, self.kind, self.Children = name, kind, list(children)\n" +
                "tree = [\n" +
                "    Node('folder', 'folder',\n" +
                "        Node('test A', 'test', Node('r1', 'result'), Node('r2', 'result'))),\n" +
                "    Node('test B', 'test', Node('r3', 'result')),\n" +
                "    Node('stray result', 'result'),\n" +
                "]\n" +
                "found = _util.flatten(tree,\n" +
                "    is_leaf=lambda n: n.kind == 'test',\n" +
                "    is_group=lambda n: len(n.Children) > 0)\n" +
                "print(','.join(n.name for n in found))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("test A,test B", outw.ToString());
        }

        [Fact]
        public void UtilFlattenWithPath_TracksFolders_AndYieldsGroupLikeLeaves()
        {
            // Same regression shape as flatten: SavedViewpointAnimation derives from
            // GroupItem but must be yielded as ONE leaf, not descended into.
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import _util\n" +
                "class Node(object):\n" +
                "    def __init__(self, name, kind, *children):\n" +
                "        self.DisplayName, self.kind, self.Children = name, kind, list(children)\n" +
                "tree = [\n" +
                "    Node('Folder A', 'folder',\n" +
                "        Node('vp1', 'viewpoint'),\n" +
                "        Node('anim', 'animation', Node('cut1', 'cut'))),\n" +
                "    Node('vp2', 'viewpoint'),\n" +
                "]\n" +
                "for folders, item in _util.flatten_with_path(tree, is_folder=lambda n: n.kind == 'folder'):\n" +
                "    print('/'.join(folders) + ':' + item.DisplayName)",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            var text = outw.ToString().Replace("\r", "");
            Assert.Contains("Folder A:vp1\n", text);
            Assert.Contains("Folder A:anim\n", text);
            Assert.Contains(":vp2\n", text);
            Assert.DoesNotContain("cut1", text);
        }

        [Fact]
        public void Markdown_ConvertsHeadersEmphasisCodeListsAndLinks()
        {
            var r = Run(
                "from pynavis import _markdown\n" +
                "h = _markdown.to_html\n" +
                "assert h('# Title') == '<h1>Title</h1>', h('# Title')\n" +
                "assert h('## Sub') == '<h2>Sub</h2>'\n" +
                "assert '<strong>bold</strong>' in h('**bold**')\n" +
                "assert '<em>it</em>' in h('*it*')\n" +
                "assert '<code>x=1</code>' in h('`x=1`')\n" +
                "out = h('- one\\n- two')\n" +
                "assert '<ul>' in out and '<li>one</li>' in out and '<li>two</li>' in out, out\n" +
                "assert '<a href=\"https://x.io\">docs</a>' in h('[docs](https://x.io)')\n" +
                "assert '&lt;script&gt;' in h('<script>'), h('<script>')\n" +
                "block = h('```\\ncode <here>\\n```')\n" +
                "assert '<pre class=\"codeblock\">' in block and 'code &lt;here&gt;' in block, block\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void OutputModule_RichHelpers_Exist_AndTableHtmlEscapes()
        {
            var r = Run(
                "from pynavis import output\n" +
                "for name in ('print_html', 'print_md', 'print_table', 'progress', 'element_link', 'format_table'):\n" +
                "    assert callable(getattr(output, name)), name\n" +
                "html = output.table_html([['<b>A</b>', 1]], ['Name', 'N'])\n" +
                "assert '&lt;b&gt;A&lt;/b&gt;' in html, html\n" +
                "assert '<table class=\"pynavis\">' in html\n" +
                "assert '<th>Name</th>' in html\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void OutputModule_PrintCode_EscapesIntoMonospaceBlock()
        {
            // print_code funnels through the module-level print_html; capture it
            // by monkeypatching the name print_code's own body resolves at call
            // time, so this never has to touch a real (headless-unsafe) window.
            var r = Run(
                "from pynavis import output\n" +
                "captured = []\n" +
                "output.print_html = lambda html: captured.append(html)\n" +
                "output.print_code('<b>hi</b> & \"quote\"')\n" +
                "html = captured[0]\n" +
                "assert html == '<pre class=\"pynavis-code\">&lt;b&gt;hi&lt;/b&gt; &amp; \"quote\"</pre>', html\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void FormsModule_UsesFluentDialogs_NotWinForms()
        {
            // Forms must route to the runtime's Fluent WPF dialogs. WinForms
            // (the 1990s look) must be gone; the python API surface stays identical.
            var r = Run(
                "import inspect\n" +
                "from pynavis import forms\n" +
                "for name in ('alert', 'confirm', 'ask_string', 'save_file', 'open_file'):\n" +
                "    assert callable(getattr(forms, name)), name\n" +
                "src = inspect.getsource(forms)\n" +
                "assert 'System.Windows.Forms' not in src, 'WinForms still referenced'\n" +
                "assert 'Dialogs' in src, 'not routed to runtime Fluent dialogs'");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ScriptModule_IsDarkTheme_ReturnsBool_WithoutRibbon()
        {
            // Outside Navisworks the AdWindows sample throws -> detector falls back to
            // light unless config overrides. Contract: always a bool, never an error.
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import script\n" +
                "print('dark=%s' % script.is_dark_theme())",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Matches("dark=(True|False)", outw.ToString());
        }

        [Fact]
        public void CommandHistory_RecallsPrevAndNext_SkipsConsecutiveDupes()
        {
            var r = Run(
                "from pynavis._history import CommandHistory\n" +
                "h = CommandHistory()\n" +
                "h.add('first')\n" +
                "h.add('second')\n" +
                "h.add('second')\n" +          // consecutive dupe collapses
                "assert h.previous() == 'second'\n" +
                "assert h.previous() == 'first'\n" +
                "assert h.previous() == 'first'\n" +   // clamped at oldest
                "assert h.next() == 'second'\n" +
                "assert h.next() == ''\n" +            // past newest -> empty draft
                "h.add('third')\n" +
                "assert h.previous() == 'third'\n");   // add resets navigation

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ReplExecute_EchoesTrailingExpression_AfterStatements()
        {
            // Regression: "import x; x.f()" fell into plain exec and printed nothing.
            var r = Run(
                "from pynavis import _repl\n" +
                "ns = {}\n" +
                "assert _repl.execute('import math; math.floor(2.5)', ns) == '2\\n'\n" +
                "assert _repl.execute('1 + 1', ns) == '2\\n'\n" +
                "assert _repl.execute('None', ns) == ''");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ReplExecute_PersistsNamespace_AndStaysSilentOnStatements()
        {
            var r = Run(
                "from pynavis import _repl\n" +
                "ns = {}\n" +
                "assert _repl.execute('x = 41', ns) == ''\n" +
                "assert _repl.execute('x + 1', ns) == '42\\n'");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ReplExecute_CapturesPrint_AndTracebacks()
        {
            var r = Run(
                "from pynavis import _repl\n" +
                "ns = {}\n" +
                "assert _repl.execute('print(\"hi\")', ns) == 'hi\\n'\n" +
                "out = _repl.execute('1/0', ns)\n" +
                "assert 'ZeroDivisionError' in out, out\n" +
                "out = _repl.execute('def broken(:', ns)\n" +
                "assert 'SyntaxError' in out, out");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ImportingApiModule_OutsideNavisworks_GivesHelpfulError()
        {
            var r = Run("import pynavis.doc");

            Assert.False(r.Succeeded);
            Assert.Contains("inside a Navisworks session", r.ErrorText);
        }

        [Fact]
        public void ToastModule_ExposesFourLevels_AndNeverThrowsOutsideNavisworks()
        {
            var r = Run(
                "from pynavis import toast\n" +
                "for name in ('show', 'success', 'error', 'info', 'warning'):\n" +
                "    assert callable(getattr(toast, name)), name\n" +
                "toast.success('unit test', 'detail line')\n" +   // no UI thread here: must not raise
                "toast.error('unit test')\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void BannerModule_MirrorsToast_AndNeverThrowsOutsideNavisworks()
        {
            var r = Run(
                "from pynavis import banner\n" +
                "for name in ('show', 'success', 'error', 'info', 'warning', 'clear'):\n" +
                "    assert callable(getattr(banner, name)), name\n" +
                "banner.success('unit test', 'detail line')\n" +   // no UI thread here: must not raise
                "banner.error('unit test')\n" +
                "banner.prompt('unit test prompt', 'stays until cleared')\n" +
                "banner.show('info', 'unit test', None, seconds=2)\n" +
                "banner.clear()\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void FacesModule_PureHalf_ImportsWithoutNavisworks_AndPairsParallelFaces()
        {
            // The API half (faces_under) imports Navisworks lazily, so the
            // module itself loads anywhere; the pure half is what True Distance
            // and Clear Clash both lean on.
            var r = Run(
                "from pynavis import faces\n" +
                "tri = [((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (1.0, 1.0, 0.0)),\n" +
                "       ((0.0, 0.0, 0.0), (1.0, 1.0, 0.0), (0.0, 1.0, 0.0))]\n" +
                "found = faces.faces_at((0.5, 0.25, 0.0), tri, 1e-6)\n" +
                "assert len(found) == 1 and abs(abs(found[0][2]) - 1.0) < 1e-9, found\n" +
                "assert faces.faces_at((0.5, 0.25, 0.5), tri, 1e-6) == []\n" +
                "n, source, angle = faces.choose([(0, 0, 1)], [(0, 0, -1)], (0, 0, 1))\n" +
                "assert source == 'pair' and angle < 1e-9, (source, angle)\n" +
                "n, source, angle = faces.choose([(0, 0, 1)], [(1, 0, 0)], (0, 0, 1))\n" +
                "assert source == 'first' and abs(angle - 90) < 1e-9, (source, angle)\n" +
                "assert faces.side_of([(0, 0, 5), (0, 0, 7)], (0, 0, 0), (0, 0, -1)) == (0.0, 0.0, 1.0)\n" +
                "assert faces.side_of([(0, 0, 5), (0, 0, 7)], (0, 0, 0), (0, 0, 1)) == (0, 0, 1)\n" +
                "assert faces.tolerance_for([(0, 0, 0)], 0.001) == 0.5\n" +
                "assert callable(faces.faces_under) and callable(faces.candidates_at)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PickModule_ImportsWithoutNavisworks_AndReadsAFinishedSession()
        {
            // pick.py only reaches Navisworks through PickService, so the module
            // itself loads anywhere. What is worth pinning here is the seam the
            // blocking pick stands on: a Python handler on the CLR Ended event,
            // and the translation of a finished session into a Hit.
            var r = Run(
                "from pynavis import pick\n" +
                "from PyNavis.Runtime.Pick import PickCancelReason, PickHit, PickSession, PickSnap\n" +
                "from System import Array, Double\n" +
                "for name in ('point', 'point_then', 'cancel'):\n" +
                "    assert callable(getattr(pick, name)), name\n" +
                "assert issubclass(pick.Unavailable, RuntimeError)\n" +
                "fired = []\n" +
                "session = PickSession('Click a point')\n" +
                "session.Activate()\n" +
                "session.Ended += lambda sender, args: fired.append(sender)\n" +
                "hit = PickHit()\n" +
                "hit.Point = Array[Double]([1.0, 2.0, 3.0])\n" +
                "hit.Normal = Array[Double]([0.0, 0.0, 1.0])\n" +
                "hit.Snap = PickSnap.LineMiddle\n" +
                "session.Complete(hit)\n" +
                "assert len(fired) == 1, len(fired)\n" +
                "answer = pick._hit(session)\n" +
                "assert answer.point == (1.0, 2.0, 3.0), answer.point\n" +
                "assert answer.normal == (0.0, 0.0, 1.0), answer.normal\n" +
                "assert answer.snap == 'line-middle', answer.snap\n" +
                "assert answer.item is None\n" +
                "cancelled = PickSession('x')\n" +
                "cancelled.Activate()\n" +
                "cancelled.Cancel(PickCancelReason.Escape)\n" +
                "assert pick._hit(cancelled) is None\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PickModule_MeasuredClick_TellsAClickFromAClearFromNothing()
        {
            // measure_point() has no event to wait on, only two readings of the
            // measurement to compare, so the comparison is the whole contract:
            // a click on a fresh measurement is its first point, a click on a
            // half-finished one is its end point, a cleared one is a cancel.
            var r = Run(
                "from pynavis import pick\n" +
                "assert callable(pick.measure_point)\n" +
                "a, b, c = (1.0, 2.0, 3.0), (4.0, 5.0, 6.0), (7.0, 8.0, 9.0)\n" +
                "click = pick.measured_click\n" +
                "assert click((None, None), (None, None)) == ('waiting', None)\n" +
                "assert click((a, b), (a, b)) == ('waiting', None)\n" +
                "assert click((None, None), (a, None)) == ('point', a)\n" +
                "assert click((a, b), (c, None)) == ('point', c)\n" +      // new measurement over a finished one
                "assert click((a, None), (a, b)) == ('point', b)\n" +      // finishing a half-done one
                "assert click((a, b), (a, c)) == ('point', c)\n" +
                "assert click((a, b), (None, None)) == ('cancelled', None)\n" +
                "assert click((a, None), (None, None)) == ('cancelled', None)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void PickModule_MeasuredLine_ReportsFirstPointThenTheLine_AndAClearAsCancel()
        {
            // measure_points() waits for a whole point-to-point measurement
            // on the same two-readings comparison: a first point alone is
            // progress (the prompt moves on), both points are the answer, a
            // measurement that vanished is the user's right-click.
            var r = Run(
                "from pynavis import pick\n" +
                "assert callable(pick.measure_points)\n" +
                "a, b, c = (1.0, 2.0, 3.0), (4.0, 5.0, 6.0), (7.0, 8.0, 9.0)\n" +
                "line = pick.measured_line\n" +
                "assert line((None, None), (None, None)) == ('waiting', None)\n" +
                "assert line((a, None), (a, None)) == ('waiting', None)\n" +
                "assert line((a, b), (a, b)) == ('waiting', None)\n" +          // the stale pair the pick began over
                "assert line((None, None), (a, None)) == ('first', a)\n" +
                "assert line((a, b), (c, None)) == ('first', c)\n" +           // new measurement over a finished one
                "assert line((a, None), (c, None)) == ('first', c)\n" +
                "assert line((a, None), (a, b)) == ('line', (a, b))\n" +
                "assert line((None, None), (a, b)) == ('line', (a, b))\n" +    // both points landed between two polls
                "assert line((a, b), (None, None)) == ('cancelled', None)\n" +
                "assert line((a, None), (None, None)) == ('cancelled', None)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ViewModule_ExposesZoomIsolateAndUnhide_AndImportsWithoutNavisworks()
        {
            // Every Navisworks import in view.py is deferred into the function
            // bodies, the way viewpoints.transaction does it, so the module
            // itself loads outside a session and its surface stays inspectable.
            var r = Run(
                "from pynavis import view\n" +
                "for name in ('zoom_selected', 'isolate', 'unhide_all'):\n" +
                "    assert callable(getattr(view, name)), name\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ScriptModule_ClipboardText_ReturnsAString_AndNeverThrows()
        {
            // Select by IDs reads the clipboard on every run without being
            // asked, so a clipboard another process is holding open, or a
            // non-STA thread like this test's, has to come back empty rather
            // than take the tool down with it.
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import script\n" +
                "value = script.clipboard_text()\n" +
                "assert isinstance(value, str), type(value)\n" +
                "print('clipboard ok')", outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("clipboard ok", outw.ToString());
        }

        [Fact]
        public void Memory_SerializeDeserialize_RoundTrips_AndRejectsBadInput()
        {
            var r = Run(
                "from pynavis import memory\n" +
                "s = memory.new_state('D:\\\\Models\\\\Tower.nwf', [{'m': 'A.nwc', 'i': 0, 'p': '1/2'}])\n" +
                "back = memory.deserialize(memory.serialize(s))\n" +
                "assert back['items'] == s['items'], back\n" +
                "assert back['cursor'] == -1\n" +
                "assert back['version'] == memory.VERSION\n" +
                "for bad in ('not json at all', '[]', '{\"version\": 99, \"items\": []}', '{\"version\": 1}'):\n" +
                "    try:\n" +
                "        memory.deserialize(bad)\n" +
                "        raise AssertionError('accepted bad input: ' + bad)\n" +
                "    except memory.MemoryFormatError:\n" +
                "        pass\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Memory_PathFor_SeparatesSameNamedDocuments_AndHandlesUnsaved()
        {
            var r = Run(
                "from pynavis import memory\n" +
                "a = memory.path_for('D:\\\\Site A\\\\Tower.nwf', 'C:\\\\root')\n" +
                "b = memory.path_for('D:\\\\Site B\\\\Tower.nwf', 'C:\\\\root')\n" +
                "assert a != b, 'same-named documents collided'\n" +
                "assert 'tower-' in a and a.endswith('.json'), a\n" +
                "assert a == memory.path_for('d:\\\\site a\\\\TOWER.NWF', 'C:\\\\root'), 'case sensitive'\n" +
                "u = memory.path_for('', 'C:\\\\root')\n" +
                "assert 'untitled-' in u, u\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Memory_SetOperations_PreserveOrder_AndDedupe()
        {
            var r = Run(
                "from pynavis import memory\n" +
                "def e(model, path):\n" +
                "    return {'m': model, 'i': 0, 'p': path}\n" +
                "a = [e('A.nwc', '1'), e('A.nwc', '2'), e('B.nwc', '1')]\n" +
                "b = [e('A.nwc', '2'), e('C.nwc', '9')]\n" +
                "assert [x['p'] for x in memory.union(a, b)] == ['1', '2', '1', '9']\n" +
                "assert [x['m'] for x in memory.union(a, b)] == ['A.nwc', 'A.nwc', 'B.nwc', 'C.nwc']\n" +
                "assert [x['p'] for x in memory.difference(a, b)] == ['1', '1']\n" +
                "assert [x['p'] for x in memory.intersection(a, b)] == ['2']\n" +
                "assert memory.union([], []) == []\n" +
                "assert memory.difference(a, []) == a\n" +
                "assert memory.intersection(a, []) == []\n" +
                "assert memory.union(a, a) == a, 'union with self must dedupe'\n" +
                "dupes = [e('A.nwc', '1'), e('a.NWC', '1')]\n" +
                "assert len(memory.union(dupes, [])) == 1, 'model name compare must ignore case'\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Memory_Step_WrapsAtBothEnds_AndCopesWithEmpty()
        {
            var r = Run(
                "from pynavis import memory\n" +
                "assert memory.step(-1, 3, 1) == 0, 'first Next starts at the top'\n" +
                "assert memory.step(-1, 3, -1) == 2, 'first Prev starts at the bottom'\n" +
                "assert memory.step(0, 3, 1) == 1\n" +
                "assert memory.step(2, 3, 1) == 0, 'wrap forward'\n" +
                "assert memory.step(0, 3, -1) == 2, 'wrap backward'\n" +
                "assert memory.step(0, 1, 1) == 0, 'single item stays put'\n" +
                "assert memory.step(-1, 0, 1) == -1, 'empty memory has no cursor'\n" +
                "assert memory.step(5, 3, 1) == 0, 'cursor past the end recovers'\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Memory_SaveLoad_RoundTripsThroughDisk_AndToleratesCorruptFiles()
        {
            var r = Run(
                "import os, shutil, tempfile\n" +
                "from pynavis import memory\n" +
                "root = os.path.join(tempfile.gettempdir(), 'pynavis_memtest')\n" +
                "if os.path.isdir(root):\n" +
                "    shutil.rmtree(root)\n" +
                "doc = 'D:\\\\Models\\\\Tower.nwf'\n" +
                "assert memory.load(doc, root)['items'] == [], 'missing file must read as empty'\n" +
                "state = memory.new_state(doc, [{'m': 'A.nwc', 'i': 0, 'p': '1/2'}])\n" +
                "path = memory.save(state, root)\n" +
                "assert os.path.exists(path), path\n" +
                "assert not os.path.exists(path + '.tmp'), 'temp file left behind'\n" +
                "assert memory.load(doc, root)['items'] == state['items']\n" +
                "state['items'] = []\n" +
                "memory.save(state, root)\n" +
                "assert memory.load(doc, root)['items'] == []\n" +
                "f = open(path, 'w')\n" +
                "f.write('{ truncated')\n" +
                "f.close()\n" +
                "try:\n" +
                "    memory.load(doc, root)\n" +
                "    raise AssertionError('corrupt file was accepted')\n" +
                "except memory.MemoryFormatError:\n" +
                "    pass\n" +
                "shutil.rmtree(root)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Memory_ApplyEntries_ResetsTheCursor()
        {
            // Every action that changes what memory holds must drop the stepper back to
            // the top, or Next would resume against a list that no longer exists.
            var r = Run(
                "from pynavis import memory\n" +
                "state = memory.new_state('doc', [{'m': 'A', 'i': 0, 'p': '1'}])\n" +
                "state['cursor'] = 0\n" +
                "updated = memory.apply_entries(state, [{'m': 'A', 'i': 0, 'p': '2'}])\n" +
                "assert updated['cursor'] == -1, updated\n" +
                "assert updated['saved'], 'saved timestamp not stamped'\n" +
                "assert [x['p'] for x in updated['items']] == ['2']\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Memory_ActionsExist_AndReturnResults()
        {
            var r = Run(
                "from pynavis import memory\n" +
                "for name in ('memorize', 'recall', 'add', 'subtract', 'intersect', 'clear',\n" +
                "             'step_next', 'step_prev', 'contents', 'save_as_set', 'purge'):\n" +
                "    assert callable(getattr(memory, name)), name\n" +
                "r = memory.Result('error', 'nope', 'detail', 3)\n" +
                "assert (r.level, r.message, r.detail, r.count) == ('error', 'nope', 'detail', 3)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Memory_PurgeTargets_OnlyMatchesMemoryFilesInTheRootItself()
        {
            var r = Run(
                "import os, shutil, tempfile\n" +
                "from pynavis import memory\n" +
                "root = os.path.join(tempfile.gettempdir(), 'pynavis_purgetest')\n" +
                "if os.path.isdir(root):\n" +
                "    shutil.rmtree(root)\n" +
                "os.makedirs(os.path.join(root, 'nested'))\n" +
                "def touch(path):\n" +
                "    f = open(path, 'w')\n" +
                "    f.write('{}')\n" +
                "    f.close()\n" +
                "touch(memory.path_for('D:\\\\A.nwf', root))\n" +
                "touch(memory.path_for('D:\\\\B.nwf', root))\n" +
                "touch(os.path.join(root, 'notes.txt'))\n" +
                "touch(os.path.join(root, 'random.json'))\n" +
                "nested_name = os.path.basename(memory.path_for('D:\\\\C.nwf', root))\n" +
                "touch(os.path.join(root, 'nested', nested_name))\n" +
                "targets = memory.purge_targets(root)\n" +
                "assert len(targets) == 2, targets\n" +
                "assert all(t.endswith('.json') for t in targets)\n" +
                "assert all(os.path.dirname(t) == root for t in targets), 'recursed out of the root'\n" +
                "assert memory.purge_targets(os.path.join(root, 'does_not_exist')) == []\n" +
                "shutil.rmtree(root)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Memory_ContentsHtml_LinksResolvedRows_AndMutesMissingOnes()
        {
            var r = Run(
                "from pynavis import memory\n" +
                "state = memory.new_state('D:\\\\Tower.nwf', [{'m': 'A.nwc', 'i': 0, 'p': '1'}])\n" +
                "state['saved'] = '2026-09-20T14:32:11'\n" +
                "rows = [('A.nwc', 'Duct D-104', '<a href=\"#\">Duct D-104</a>'),\n" +
                "        ('B.nwc', 'Beam B-2', None)]\n" +
                "html = memory.contents_html(rows, state)\n" +
                "assert 'D:\\\\Tower.nwf' in html, html\n" +
                "assert '2026-09-20T14:32:11' in html\n" +
                "assert '<a href=\"#\">Duct D-104</a>' in html, 'link markup was escaped'\n" +
                "assert 'not in this model' in html\n" +
                "assert '<td class=\"muted\">' in html\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ScriptModule_GetBundleFile_ReturnsNoneOutsideCommandContext()
        {
            // no command context in tests: CommandPath is whatever last ran, so point it nowhere
            var r = Run(
                "from pynavis import script\n" +
                "assert script.get_bundle_file('no_such_file_ever.xyz') is None");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void DatafilesModule_PathFor_SeparatesDocuments_ByNameAndHash()
        {
            var r = Run(
                "from pynavis import _datafiles\n" +
                "import tempfile, os\n" +
                "root = tempfile.mkdtemp()\n" +
                "p1 = _datafiles.path_for(root, 'cfg_my_tool')\n" +
                "assert p1 == os.path.join(root, 'cfg_my_tool.json')\n" +
                "p2 = _datafiles.path_for(root, 'cfg_my_tool', doc_path=r'C:\\Jobs\\Tower.NWF')\n" +
                "p3 = _datafiles.path_for(root, 'cfg_my_tool', doc_path=r'c:\\jobs\\TOWER.nwf')\n" +
                "assert p2 == p3\n" +
                "assert 'tower' in os.path.basename(p2)\n" +
                "data = _datafiles.load_all(p1)\n" +
                "assert data == {}\n" +
                "_datafiles.store(p1, 'k', [1, 2])\n" +
                "assert _datafiles.load_all(p1) == {'k': [1, 2]}\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        // store() is read-modify-write: a corrupt file used to load as {} and get
        // overwritten, quietly costing every other key. The bad file is kept aside.
        [Fact]
        public void DatafilesModule_Store_OverCorruptFile_KeepsTheOldBytesAside()
        {
            var r = Run(
                "from pynavis import _datafiles\n" +
                "import tempfile, os\n" +
                "root = tempfile.mkdtemp()\n" +
                "p = _datafiles.path_for(root, 'cfg_my_tool')\n" +
                "f = open(p, 'w'); f.write('{\"precious\": 1, broken'); f.close()\n" +
                "_datafiles.store(p, 'k', 2)\n" +
                "assert _datafiles.load_all(p) == {'k': 2}\n" +
                "f = open(p + '.corrupt', 'r'); kept = f.read(); f.close()\n" +
                "assert kept == '{\"precious\": 1, broken', kept\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void DatafilesAndSettings_Save_LeaveNoTempFileBehind()
        {
            var r = Run(
                "from pynavis import _datafiles, settings\n" +
                "import tempfile, os\n" +
                "root = tempfile.mkdtemp()\n" +
                "p = _datafiles.path_for(root, 'cfg_my_tool')\n" +
                "_datafiles.store(p, 'a', 1)\n" +
                "_datafiles.store(p, 'b', 2)\n" +
                "assert os.listdir(root) == ['cfg_my_tool.json'], os.listdir(root)\n" +
                "assert _datafiles.load_all(p) == {'a': 1, 'b': 2}\n" +
                "settings._APPDATA_OVERRIDE = root\n" +
                "try:\n" +
                "    settings.save('t9', {'a': 1})\n" +
                "    settings.save('t9', {'a': 2})\n" +
                "    folder = os.path.dirname(settings.path_for('t9'))\n" +
                "    assert os.listdir(folder) == ['t9.json'], os.listdir(folder)\n" +
                "    assert settings.load('t9', {'a': 0}) == {'a': 2}\n" +
                "finally:\n" +
                "    settings._APPDATA_OVERRIDE = None\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        // The atomic writer is shared, so its contract is pinned directly: a failed
        // write must leave the previous content in place, not a truncated file.
        [Fact]
        public void AtomicWrite_FailureMidWrite_LeavesThePreviousFileIntact()
        {
            var r = Run(
                "from pynavis import _atomic\n" +
                "import tempfile, os\n" +
                "root = tempfile.mkdtemp()\n" +
                "p = os.path.join(root, 'x.json')\n" +
                "_atomic.write_text(p, 'first')\n" +
                "try:\n" +
                "    _atomic.write_text(p, 12345)   # not text: the handle rejects it after the temp file opened\n" +
                "    raise AssertionError('expected the write to fail')\n" +
                "except TypeError:\n" +
                "    pass\n" +
                "f = open(p, 'r'); text = f.read(); f.close()\n" +
                "assert text == 'first', text\n" +
                "assert os.listdir(root) == ['x.json'], os.listdir(root)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        // The command context is ambient and outlives its run, so save_config() called
        // from a modeless dialog or a dock-pane handler AFTER the user ran something else
        // used to write under that other command's key. script.bind() pins the identity
        // while the command is still the one running.
        [Fact]
        public void BoundScript_KeepsItsOwnKey_AfterAnotherCommandHasRun()
        {
            var host = Execution.PyNavisHost.Instance;
            host.SetCommandContext(null, null, "Tool A", "Smoke.tab/P.panel/A.pushbutton");

            var r = Run(
                "from pynavis import script, settings\n" +
                "import tempfile, os\n" +
                "settings._APPDATA_OVERRIDE = tempfile.mkdtemp()\n" +
                "try:\n" +
                "    me = script.bind()\n" +
                "    # the user clicks another button while A's modeless dialog is still open\n" +
                "    script.get_host().SetCommandContext(None, None, 'Tool B', 'Smoke.tab/P.panel/B.pushbutton')\n" +
                "    me.save_config({'width': 300})\n" +
                "    a = settings.path_for(script._config_key('Smoke.tab/P.panel/A.pushbutton'))\n" +
                "    b = settings.path_for(script._config_key('Smoke.tab/P.panel/B.pushbutton'))\n" +
                "    assert os.path.exists(a), 'A did not get its own settings'\n" +
                "    assert not os.path.exists(b), 'A wrote under B'\n" +
                "    assert me.get_config({'width': 0}) == {'width': 300}\n" +
                "    assert script.get_config({'width': 0}) == {'width': 0}   # ambient is B now\n" +
                "    me.reset_config()\n" +
                "    assert not os.path.exists(a)\n" +
                "finally:\n" +
                "    settings._APPDATA_OVERRIDE = None\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void AllShippedModules_Compile()
        {
            var sources = Directory.GetFiles(PyNavisLibDir, "*.py", SearchOption.AllDirectories);
            Assert.NotEmpty(sources);

            foreach (var path in sources)
            {
                var code = "with open(__source_path__) as f:\n"
                         + "    compile(f.read(), __source_path__, 'exec')";
                var req = new ScriptRequest { Code = code };
                req.Globals["__source_path__"] = path;

                var r = _engine.Execute(req);
                Assert.True(r.Succeeded, $"{Path.GetFileName(path)} failed to compile:\n{r.ErrorText}");
            }
        }

        [Fact]
        public void Logger_FiltersLevel_AndInvokesCustomSink()
        {
            var r = Run(
                "from pynavis import _log\n" +
                "records = []\n" +
                "logger = _log.Logger('test', sink=lambda level, source, msg: records.append((level, source, msg)))\n" +
                "logger.debug('hidden')\n" +
                "logger.info('shown')\n" +
                "logger.set_level('debug')\n" +
                "logger.debug('now shown')\n" +
                "logger.error('boom')\n" +
                "assert records == [('info', 'test', 'shown'), ('debug', 'test', 'now shown'), ('error', 'test', 'boom')], repr(records)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Logger_Names_The_Hook_That_Is_Running_Not_The_Last_Command()
        {
            // A hook never calls SetCommandContext, so CommandTitle still holds whatever
            // button ran last. Attributing a hook's log lines to that button is wrong and
            // actively misleading: field-measured, lines from the
            // viewpoint-recalled hook were logged as "[Reload]" because Reload was the
            // last thing clicked.
            var host = Execution.PyNavisHost.Instance;
            host.SetCommandContext(null, null, "Reload", "pyNavis.tab/pyNavis.panel/Reload.pushbutton");
            host.LogSource = "hook Smoke:viewpoint-recalled";
            try
            {
                var r = Run(
                    "from pynavis import script\n" +
                    "logger = script.get_logger()\n" +
                    "assert logger._source == 'hook Smoke:viewpoint-recalled', repr(logger._source)\n");

                Assert.True(r.Succeeded, r.ErrorText);
            }
            finally
            {
                host.LogSource = null;
            }
        }

        [Fact]
        public void Logger_Falls_Back_To_The_Command_Title_When_No_Scope_Is_Set()
        {
            // Ordinary button runs must keep naming the command, so the fix cannot simply
            // replace the title.
            var host = Execution.PyNavisHost.Instance;
            host.LogSource = null;
            host.SetCommandContext(null, null, "Memorize", "Smoke.tab/P.panel/Remember.pushbutton");

            var r = Run(
                "from pynavis import script\n" +
                "logger = script.get_logger()\n" +
                "assert logger._source == 'Memorize', repr(logger._source)\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void Starting_A_Command_Clears_A_Leftover_Log_Scope()
        {
            // The mirror of the original bug: a hook's label must not leak onto the next
            // button the user clicks.
            var host = Execution.PyNavisHost.Instance;
            host.LogSource = "hook Smoke:camera-moved";
            host.SetCommandContext(null, null, "Memorize", "Smoke.tab/P.panel/Remember.pushbutton");

            Assert.Null(host.LogSource);
        }

        [Fact]
        public void ScriptModule_ConfigHelpers_KeyConfigKeyAndLoadSave()
        {
            var r = Run(
                "from pynavis import script, settings\n" +
                "import tempfile, os\n" +
                "assert script._config_key('pyNavis.tab/Tools.panel/My Tool.pushbutton') == 'cfg_pynavis_tab_tools_panel_my_tool_pushbutton'\n" +
                "settings._APPDATA_OVERRIDE = tempfile.mkdtemp()\n" +
                "try:\n" +
                "    key = script._config_key('a/b.pushbutton')\n" +
                "    saved = settings.load(key, {'x': 1})\n" +
                "    assert saved == {'x': 1}\n" +
                "finally:\n" +
                "    settings._APPDATA_OVERRIDE = None\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ChartsModule_RendersThemeAwareSvg_AndEscapesText()
        {
            var r = Run(
                "from pynavis import _charts\n" +
                "svg = _charts.bar_svg(['A', 'B'], [3, 9], title='T')\n" +
                "assert svg.startswith('<svg') and svg.endswith('</svg>')\n" +
                "assert 'var(--chart-1)' in svg\n" +
                "assert 'T' in svg and '9' in svg\n" +
                "# scaling: the 9 bar is 3x the 3 bar\n" +
                "assert svg.count('<rect') >= 2\n" +
                "# escaping\n" +
                "svg2 = _charts.bar_svg(['<b>'], [1])\n" +
                "assert '<b>' not in svg2 and '&lt;b&gt;' in svg2\n" +
                "# degenerate\n" +
                "assert 'no data' in _charts.bar_svg([], [])\n" +
                "pie = _charts.pie_svg(['A', 'B'], [1, 1])\n" +
                "assert '50%' in pie\n" +
                "line = _charts.line_svg(['x1', 'x2'], {'s': [1, 2]})\n" +
                "assert '<polyline' in line\n" +
                "# regression: a series longer than the label axis has a never-plotted\n" +
                "# tail that must not scale the chart - the truncated-to-2 rendering must\n" +
                "# be pixel-identical whether or not that unplotted tail exists\n" +
                "truncated = _charts.line_svg(['a', 'b'], {'s1': [1, 2], 's2': [1, 2]})\n" +
                "with_tail = _charts.line_svg(['a', 'b'], {'s1': [1, 2], 's2': [1, 2, 100]})\n" +
                "assert with_tail == truncated, with_tail\n" +
                "# regression: end labels anchor inward. Centred on the first and last\n" +
                "# point they overhang the viewBox and the SVG clips them.\n" +
                "edges = _charts.line_svg(['first', 'mid', 'last'], {'s': [1, 2, 3]})\n" +
                "assert 'text-anchor=\"start\"' in edges and 'text-anchor=\"end\"' in edges, edges\n" +
                "assert edges.count('text-anchor=\"middle\"') == 1, edges\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void XlModule_WritesAndReadsXlsx_RoundTrip()
        {
            var r = Run(
                "from pynavis import xl, _xlsx\n" +
                "import tempfile, os\n" +
                "assert _xlsx.column_letter(0) == 'A'\n" +
                "assert _xlsx.column_letter(25) == 'Z'\n" +
                "assert _xlsx.column_letter(26) == 'AA'\n" +
                "assert _xlsx.column_letter(51) == 'AZ'\n" +
                "assert _xlsx.column_letter(701) == 'ZZ'\n" +
                "assert _xlsx.column_letter(702) == 'AAA'\n" +
                "\n" +
                "path = os.path.join(tempfile.mkdtemp(), 'roundtrip.xlsx')\n" +
                "rows = [['Wall', 42, 1.5, True], ['Door & Frame', 0, -2.25, False], ['<Odd> \"Name\"', 7, 0.0, True]]\n" +
                "xl.write(path, rows, headers=['Type', 'Count', 'Offset', 'Fire'])\n" +
                "back = xl.read(path)\n" +
                "assert back[0] == ['Type', 'Count', 'Offset', 'Fire']\n" +
                "assert back[1][0] == 'Wall' and back[1][1] == 42.0 and back[1][2] == 1.5\n" +
                "assert back[2][0] == 'Door & Frame' and back[2][2] == -2.25\n" +
                "assert back[3][0] == '<Odd> \"Name\"'\n" +
                "assert xl.sheets(path) == ['Sheet1']\n" +
                "\n" +
                "# two sheets\n" +
                "path2 = os.path.join(tempfile.mkdtemp(), 'two.xlsx')\n" +
                "xl.write(path2, [['a']], sheet='First')\n" +
                "xl.write(path2, [['b']], sheet='Second')\n" +
                "assert sorted(xl.sheets(path2)) == ['First', 'Second']\n" +
                "assert xl.read(path2, sheet='Second') == [['b']]\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void XlModule_RoundTrip_CoversBooleanFalse_ZeroValues_AndFullRows()
        {
            // Closes a coverage gap in the brief's normative round-trip test: it never
            // asserted on back[1][3], back[2][1], back[2][3], or back[3] in full, so a
            // regression in False/zero handling could pass silently.
            var r = Run(
                "from pynavis import xl\n" +
                "import tempfile, os\n" +
                "path = os.path.join(tempfile.mkdtemp(), 'roundtrip.xlsx')\n" +
                "rows = [['Wall', 42, 1.5, True], ['Door & Frame', 0, -2.25, False], ['<Odd> \"Name\"', 7, 0.0, True]]\n" +
                "xl.write(path, rows, headers=['Type', 'Count', 'Offset', 'Fire'])\n" +
                "back = xl.read(path)\n" +
                "assert back[1] == ['Wall', 42.0, 1.5, True], back[1]\n" +
                "assert back[2] == ['Door & Frame', 0.0, -2.25, False], back[2]\n" +
                "assert back[3] == ['<Odd> \"Name\"', 7.0, 0.0, True], back[3]\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void XlModule_ParseSheet_HandlesBlankRowGaps_AndUnrecognizedCellTypes()
        {
            // Regression test for two robustness fixes found in self-review: Excel omits
            // entirely-blank rows from the XML rather than emitting an empty <row>, and
            // real workbooks can carry cell types this reader does not special-case
            // (error cells "e", ISO-date cells "d"). Both must degrade gracefully rather
            // than corrupting row alignment or crashing the reader.
            var r = Run(
                "from pynavis import _xlsx\n" +
                "sheet_xml = (\n" +
                "    '<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">'\n" +
                "    '<sheetData>'\n" +
                "    '<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>'\n" +
                "    '<row r=\"3\"><c r=\"A3\"><v>3</v></c></row>'\n" +
                "    '<row r=\"4\"><c r=\"A4\" t=\"e\"><v>#DIV/0!</v></c>'\n" +
                "    '<c r=\"B4\" t=\"d\"><v>2024-01-01</v></c></row>'\n" +
                "    '</sheetData></worksheet>'\n" +
                ")\n" +
                "rows = _xlsx.parse_sheet(sheet_xml, [])\n" +
                "assert rows == [[1.0], [], [3.0], ['#DIV/0!', '2024-01-01']], rows\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void XlModule_ParseSheet_SkipsCellsWhoseRefDoesNotParse()
        {
            // '$B$1' does not start with column letters, so _column_index cannot
            // place it. It used to return -1, and values[-1] wrote over the LAST
            // cell of the row: the A1 value silently became the B1 value.
            var r = Run(
                "from pynavis import _xlsx\n" +
                "sheet_xml = (\n" +
                "    '<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">'\n" +
                "    '<sheetData>'\n" +
                "    '<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>keep</t></is></c>'\n" +
                "    '<c r=\"$B$1\" t=\"inlineStr\"><is><t>junk</t></is></c></row>'\n" +
                "    '<row r=\"two\"><c r=\"A2\"><v>2</v></c></row>'\n" +
                "    '</sheetData></worksheet>'\n" +
                ")\n" +
                "rows = _xlsx.parse_sheet(sheet_xml, [])\n" +
                "assert rows[0] == ['keep'], rows\n" +
                "assert rows[1] == [2.0], rows\n" +
                "# a shared-string index that is not a number skips too, it does not raise\n" +
                "bad_shared = (\n" +
                "    '<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">'\n" +
                "    '<sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>x</v></c></row></sheetData></worksheet>'\n" +
                ")\n" +
                "assert _xlsx.parse_sheet(bad_shared, ['a']) == [['']], _xlsx.parse_sheet(bad_shared, ['a'])\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void XlModule_Write_PreservesSheetOrder_AndLeavesNoTempFile()
        {
            // Two regressions at once: build_workbook used to sort sheet names, so
            // rewriting 'Data' reshuffled the user's workbook to Data/Summary; and
            // write() opened the destination with zipfile 'w' (truncating) AFTER
            // reading the old workbook back, so a failure mid-write destroyed the
            // file. The rebuild now goes to a sibling .tmp and is renamed over the
            // target, which must leave no .tmp behind on the happy path.
            var r = Run(
                "from pynavis import xl\n" +
                "import tempfile, os\n" +
                "folder = tempfile.mkdtemp()\n" +
                "path = os.path.join(folder, 'ordered.xlsx')\n" +
                "xl.write(path, [['s']], headers=['Summary'], sheet='Summary')\n" +
                "xl.write(path, [['d']], headers=['Data'], sheet='Data')\n" +
                "assert xl.sheets(path) == ['Summary', 'Data'], xl.sheets(path)\n" +
                "xl.write(path, [['d2']], headers=['Data'], sheet='Data')\n" +
                "assert xl.sheets(path) == ['Summary', 'Data'], xl.sheets(path)\n" +
                "assert xl.read(path, sheet='Data') == [['Data'], ['d2']], xl.read(path, sheet='Data')\n" +
                "assert xl.read(path, sheet='Summary') == [['Summary'], ['s']]\n" +
                "leftovers = [n for n in os.listdir(folder) if n.endswith('.tmp')]\n" +
                "assert leftovers == [], leftovers\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void ChartsModule_HandlesFullCircleSlices_AndRaggedValueLists()
        {
            var r = Run(
                "from pynavis import _charts\n" +
                "# a single category is a 360 degree slice: both arc endpoints round to\n" +
                "# the same coordinates, and SVG draws nothing for a zero-length arc, so\n" +
                "# it has to come out as a circle or the chart is blank\n" +
                "pie = _charts.pie_svg(['A'], [42])\n" +
                "assert '<circle' in pie, pie\n" +
                "assert '100%' in pie\n" +
                "ring = _charts.pie_svg(['A'], [42], doughnut=True)\n" +
                "assert '<circle' in ring and 'stroke-width' in ring, ring\n" +
                "# ragged inputs: fewer values than labels used to IndexError\n" +
                "svg = _charts.bar_svg(['A', 'B', 'C'], [1, 2])\n" +
                "assert svg.count('<rect') == 2, svg\n" +
                "assert '>C<' not in svg, svg\n" +
                "wide = _charts.bar_svg(['A'] * 12, [1, 2])\n" +
                "assert wide.count('<rect') == 2, wide\n" +
                "assert _charts.pie_svg(['A', 'B', 'C'], [1]).count('<circle') == 1\n" +
                "# None and NaN are drawn as zero, never raised\n" +
                "nan = float('nan')\n" +
                "assert 'no data' not in _charts.bar_svg(['A', 'B'], [None, 5])\n" +
                "assert 'no data' in _charts.bar_svg(['A', 'B'], [None, nan])\n" +
                "assert 'no data' not in _charts.line_svg(['a', 'b'], {'s': [None, 3]})\n" +
                "assert 'no data' not in _charts.pie_svg(['A', 'B'], [nan, 3])\n");

            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void QueryModule_RoundTrip_ToAndFromDict()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import _query\n" +
                "q = _query.Query()\n" +
                "q.groups.append([_query.Condition('Item', 'Type', 'equals', 'Wall')])\n" +
                "d = q.to_dict()\n" +
                "assert d == {'or': [{'and': [{'category': 'Item', 'prop': 'Type', 'op': 'equals', 'value': 'Wall'}]}]}\n" +
                "q2 = _query.Query.from_dict(d)\n" +
                "assert q2.groups[0][0].category == 'Item' and q2.groups[0][0].op == 'equals'\n" +
                "assert _query.validate(q2) == []\n" +
                "bad = _query.Query.from_dict({'or': [{'and': [{'category': '', 'prop': 'x', 'op': 'nope', 'value': 1}]}]})\n" +
                "problems = _query.validate(bad)\n" +
                "assert any('op' in p for p in problems) and any('category' in p for p in problems)\n" +
                "# ops needing no value\n" +
                "q3 = _query.Query.from_dict({'or': [{'and': [{'category': 'Item', 'prop': 'Type', 'op': 'has_property'}]}]})\n" +
                "assert _query.validate(q3) == []\n" +
                "# an EMPTY AND group is invalid: it compiles to a search with no conditions,\n" +
                "# i.e. a set matching the WHOLE model - never what a caller meant\n" +
                "empty_group = _query.Query.from_dict({'or': [{'and': []}]})\n" +
                "problems = _query.validate(empty_group)\n" +
                "assert any('Group 0' in p and 'no conditions' in p for p in problems), problems\n" +
                "# only the empty group is flagged, not its (valid) sibling\n" +
                "mixed = _query.Query.from_dict({'or': [\n" +
                "    {'and': [{'category': 'Item', 'prop': 'Type', 'op': 'equals', 'value': 'Wall'}]},\n" +
                "    {'and': []}]})\n" +
                "problems = _query.validate(mixed)\n" +
                "assert len(problems) == 1 and 'Group 1' in problems[0], problems\n" +
                "# a query with NO groups at all is still fine (nothing to be empty)\n" +
                "assert _query.validate(_query.Query.from_dict({'or': []})) == []\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void QueryBuilder_FluentApi_BuildsValidQueries()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import _query\n" +
                "q = (_query.Builder()\n" +
                "     .prop('Item', 'Type').equals('Wall')\n" +
                "     .and_prop('Element', 'Level').contains('L1').ignore_case()\n" +
                "     .or_group()\n" +
                "     .prop('Item', 'Type').equals('Floor')\n" +
                "     .build())\n" +
                "assert len(q.groups) == 2\n" +
                "assert len(q.groups[0]) == 2 and len(q.groups[1]) == 1\n" +
                "assert q.groups[0][1].ignore_case is True\n" +
                "assert q.groups[1][0].value == 'Floor'\n" +
                "# building with a dangling .prop (no operator) raises\n" +
                "try:\n" +
                "    _query.Builder().prop('A', 'B').build()\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError:\n" +
                "    pass\n" +
                "# operator without prop raises\n" +
                "try:\n" +
                "    _query.Builder().equals(1)\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError:\n" +
                "    pass\n" +
                "# ignore_case() with a condition still PENDING raises instead of silently\n" +
                "# flagging the PREVIOUS condition (the one already in the group)\n" +
                "b = (_query.Builder()\n" +
                "     .prop('Item', 'Type').equals('Wall')\n" +
                "     .and_prop('Element', 'Level'))\n" +
                "try:\n" +
                "    b.ignore_case()\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError as error:\n" +
                "    assert 'ignore_case' in str(error), error\n" +
                "assert b.current_group[0].ignore_case is False, 'must not flag the previous condition'\n" +
                "# completing the condition first makes the flag land where it was meant to\n" +
                "q2 = b.contains('L1').ignore_case().build()\n" +
                "assert q2.groups[0][0].ignore_case is False and q2.groups[0][1].ignore_case is True\n" +
                "# ignore_case() with nothing started at all still raises\n" +
                "try:\n" +
                "    _query.Builder().ignore_case()\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError:\n" +
                "    pass\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void SetsModule_VariantKindAndSplitPath_PureHelpers()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import sets\n" +
                "# bool is checked before int - Python bool is an int subclass\n" +
                "assert sets._variant_kind(True) == 'bool'\n" +
                "assert sets._variant_kind(False) == 'bool'\n" +
                "assert sets._variant_kind(3) == 'int'\n" +
                "assert sets._variant_kind(3.5) == 'double'\n" +
                "assert sets._variant_kind('Wall') == 'string'\n" +
                "assert sets._variant_kind(None) == 'string'\n" +
                "assert sets._split_path('A/B/C') == ['A', 'B', 'C']\n" +
                "assert sets._split_path('Name') == ['Name']\n" +
                "# a literal slash in a name is escaped as backslash-slash\n" +
                "assert sets._split_path('A\\\\/B') == ['A/B']\n" +
                "assert sets._split_path('Folder/A\\\\/B/Leaf') == ['Folder', 'A/B', 'Leaf']\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void PropsModule_VariantToPython_ProbesInPinnedOrder()
        {
            // A python stub standing in for VariantData: every Is*/To* member the
            // pinned probe order in props._variant_to_python checks, each returning
            // a distinguishable value so the assertions can tell which branch fired.
            const string fakeVariant =
                "class FakeVariant(object):\n" +
                "    def __init__(self, **flags):\n" +
                "        self.IsDisplayString = flags.get('IsDisplayString', False)\n" +
                "        self.IsIdentifierString = flags.get('IsIdentifierString', False)\n" +
                "        self.IsBoolean = flags.get('IsBoolean', False)\n" +
                "        self.IsInt32 = flags.get('IsInt32', False)\n" +
                "        self.IsDateTime = flags.get('IsDateTime', False)\n" +
                "        self.IsDouble = flags.get('IsDouble', False)\n" +
                "        self.IsDoubleLength = flags.get('IsDoubleLength', False)\n" +
                "        self.IsPoint3D = flags.get('IsPoint3D', False)\n" +
                "        self._bool = flags.get('bool_value', True)\n" +
                "        self._int = flags.get('int_value', 7)\n" +
                "        self._double = flags.get('double_value', 3.5)\n" +
                "        self._point = flags.get('point_value', (1.0, 2.0, 3.0))\n" +
                "        self._datetime = flags.get('datetime_value', None)\n" +
                "    def ToString(self):\n" +
                "        return 'TOSTRING'\n" +
                "    def ToDisplayString(self):\n" +
                "        return 'DISPLAY'\n" +
                "    def ToIdentifierString(self):\n" +
                "        return 'IDENT'\n" +
                "    def ToBoolean(self):\n" +
                "        return self._bool\n" +
                "    def ToInt32(self):\n" +
                "        return self._int\n" +
                "    def ToDouble(self):\n" +
                "        return self._double\n" +
                "    def ToPoint3D(self):\n" +
                "        return self._point\n" +
                "    def ToDateTime(self):\n" +
                "        return self._datetime\n" +
                "class FakeVariantNoToDateTime(FakeVariant):\n" +
                "    def __getattribute__(self, name):\n" +
                "        if name == 'ToDateTime':\n" +
                "            raise AttributeError(name)\n" +
                "        return object.__getattribute__(self, name)\n";

            var outw = new StringWriter();
            var r = Run(
                "from pynavis import props\n" +
                "import datetime\n" +
                fakeVariant +
                "conv = props._variant_to_python\n" +
                "\n" +
                "# each probe alone reaches its own conversion\n" +
                "assert conv(FakeVariant(IsDisplayString=True)) == 'DISPLAY'\n" +
                "assert conv(FakeVariant(IsIdentifierString=True)) == 'IDENT'\n" +
                "assert conv(FakeVariant(IsBoolean=True, bool_value=False)) is False\n" +
                "assert conv(FakeVariant(IsInt32=True, int_value=42)) == 42\n" +
                "assert isinstance(conv(FakeVariant(IsInt32=True, int_value=42)), int)\n" +
                "dt = datetime.datetime(2026, 1, 2, 3, 4, 5)\n" +
                "assert conv(FakeVariant(IsDateTime=True, datetime_value=dt)) == dt\n" +
                "assert conv(FakeVariantNoToDateTime(IsDateTime=True)) == 'TOSTRING'\n" +
                "assert conv(FakeVariant(IsDouble=True, double_value=1.25)) == 1.25\n" +
                "assert isinstance(conv(FakeVariant(IsDouble=True, double_value=1.25)), float)\n" +
                "assert conv(FakeVariant(IsDoubleLength=True, double_value=9.5)) == 9.5\n" +
                "assert conv(FakeVariant(IsPoint3D=True, point_value=(1.0, 2.0, 3.0))) == (1.0, 2.0, 3.0)\n" +
                "assert conv(FakeVariant()) == 'TOSTRING'\n" +
                "\n" +
                "# PINNED ORDER: two probes True at once - the earlier one in the\n" +
                "# docstring's list must win, proving the implemented check order\n" +
                "assert conv(FakeVariant(IsDisplayString=True, IsIdentifierString=True)) == 'DISPLAY'\n" +
                "assert conv(FakeVariant(IsDisplayString=True, IsBoolean=True)) == 'DISPLAY'\n" +
                "assert conv(FakeVariant(IsIdentifierString=True, IsBoolean=True)) == 'IDENT'\n" +
                "assert conv(FakeVariant(IsBoolean=True, IsInt32=True)) is True\n" +
                "assert conv(FakeVariant(IsInt32=True, IsDateTime=True, datetime_value=dt)) == 7\n" +
                "assert conv(FakeVariant(IsDateTime=True, IsDouble=True, datetime_value=dt)) == dt\n" +
                "assert conv(FakeVariant(IsDouble=True, IsPoint3D=True, double_value=1.25)) == 1.25\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void PropsModule_SetRemoveCustom_RejectEmptyTabNameHeadlessly()
        {
            // The tab_name guard in set_custom/remove_custom fires BEFORE any lazy
            // import or item access, so it raises with zero Navisworks dependency -
            // items=[] and values={} never get touched.
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import props\n" +
                "for tab_name in ('', '   '):\n" +
                "    try:\n" +
                "        props.set_custom([], tab_name, {})\n" +
                "        assert False, 'set_custom should raise'\n" +
                "    except ValueError as error:\n" +
                "        assert 'tab_name' in str(error)\n" +
                "    try:\n" +
                "        props.remove_custom([], tab_name)\n" +
                "        assert False, 'remove_custom should raise'\n" +
                "    except ValueError as error:\n" +
                "        assert 'tab_name' in str(error)\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void SetsModule_WritePlanningHelpers_PureHelpers()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import sets\n" +
                "# _dedupe_name: unique names pass through; collisions append ' (2)', ' (3)', ...\n" +
                "assert sets._dedupe_name([], 'Walls') == 'Walls'\n" +
                "assert sets._dedupe_name(['Walls'], 'Floors') == 'Floors'\n" +
                "assert sets._dedupe_name(['Walls'], 'Walls') == 'Walls (2)'\n" +
                "assert sets._dedupe_name(['Walls', 'Walls (2)'], 'Walls') == 'Walls (3)'\n" +
                "\n" +
                "# _plan_folders: nothing exists - every segment is a create\n" +
                "assert sets._plan_folders([], 'A/B') == [('create', 'A'), ('create', 'A/B')]\n" +
                "\n" +
                "# _plan_folders: 'A' exists at root, 'A/B' does not - reuse then create\n" +
                "rows = [{'is_folder': True, 'parent_key': '', 'name': 'A', 'key': '0'}]\n" +
                "assert sets._plan_folders(rows, 'A/B') == [('reuse', 'A'), ('create', 'A/B')]\n" +
                "\n" +
                "# _plan_folders: both 'A' and 'A/B' exist - reuse both\n" +
                "rows2 = [\n" +
                "    {'is_folder': True, 'parent_key': '', 'name': 'A', 'key': '0'},\n" +
                "    {'is_folder': True, 'parent_key': '0', 'name': 'B', 'key': '0/1'},\n" +
                "]\n" +
                "assert sets._plan_folders(rows2, 'A/B') == [('reuse', 'A'), ('reuse', 'A/B')]\n" +
                "\n" +
                "# a same-named folder sitting elsewhere (not under 'A') must not be reused\n" +
                "rows3 = [\n" +
                "    {'is_folder': True, 'parent_key': '', 'name': 'A', 'key': '0'},\n" +
                "    {'is_folder': True, 'parent_key': '', 'name': 'B', 'key': '1'},\n" +
                "]\n" +
                "assert sets._plan_folders(rows3, 'A/B') == [('reuse', 'A'), ('create', 'A/B')]\n" +
                "\n" +
                "# _validate_import: structural + portability + raw-op checks, no document needed\n" +
                "assert sets._validate_import({'sets': []}) == []\n" +
                "good_query = {'or': [{'and': [\n" +
                "    {'category': 'Item', 'prop': 'Type', 'op': 'equals', 'value': 'Wall'}]}]}\n" +
                "assert sets._validate_import({'sets': [{'name': 'Walls', 'query': good_query}]}) == []\n" +
                "\n" +
                "problems = sets._validate_import({'sets': [{'name': '', 'query': good_query}]})\n" +
                "assert any('name' in p for p in problems)\n" +
                "\n" +
                "problems = sets._validate_import({'sets': [{'name': 'Static', 'items': 5}]})\n" +
                "assert any('not portable' in p or 'not be imported' in p for p in problems)\n" +
                "\n" +
                "raw_query = {'or': [{'and': [\n" +
                "    {'category': 'Item', 'prop': 'Type', 'op': 'raw', 'text': 'weird()'}]}]}\n" +
                "problems = sets._validate_import({'sets': [{'name': 'Weird', 'query': raw_query}]})\n" +
                "assert any('raw' in p for p in problems)\n" +
                "\n" +
                "multi_group_query = {'or': [\n" +
                "    {'and': [{'category': 'Item', 'prop': 'Type', 'op': 'equals', 'value': 'Wall'}]},\n" +
                "    {'and': [{'category': 'Item', 'prop': 'Type', 'op': 'equals', 'value': 'Floor'}]},\n" +
                "]}\n" +
                "problems = sets._validate_import({'sets': [{'name': 'Multi', 'query': multi_group_query}]})\n" +
                "assert any('OR group' in p or 'condition list' in p for p in problems)\n" +
                "\n" +
                "# an EMPTY AND group (what a blank spreadsheet row used to produce) is a\n" +
                "# match-EVERYTHING search set: rejected, and the problem names the set\n" +
                "problems = sets._validate_import({'sets': [{'name': 'S', 'query': {'or': [{'and': []}]}}]})\n" +
                "assert problems != [], 'an empty AND group must be rejected'\n" +
                "assert all(p.startswith('S:') for p in problems), problems\n" +
                "assert any('no conditions' in p for p in problems), problems\n" +
                "\n" +
                "# a query with no groups at all is rejected too, by the one-group rule\n" +
                "assert sets._validate_import({'sets': [{'name': 'None', 'query': {'or': []}}]}) != []\n" +
                "\n" +
                "assert sets._validate_import({'sets': 'nope'}) != []\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void PropsModule_PlanCustom_SortsValidatesTypes()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import props\n" +
                "# sorted by name, deterministic regardless of dict insertion order\n" +
                "assert props._plan_custom({'Fire Rating': 60, 'Checked': True}) == " +
                "[('Checked', True), ('Fire Rating', 60)]\n" +
                "assert props._plan_custom({}) == []\n" +
                "assert props._plan_custom({'A': 'x', 'B': 1.5}) == [('A', 'x'), ('B', 1.5)]\n" +
                "\n" +
                "# empty / whitespace-only name raises ValueError\n" +
                "try:\n" +
                "    props._plan_custom({'': 1})\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError:\n" +
                "    pass\n" +
                "try:\n" +
                "    props._plan_custom({'   ': 1})\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError:\n" +
                "    pass\n" +
                "\n" +
                "# unsupported value type raises, naming the type\n" +
                "try:\n" +
                "    props._plan_custom({'Bad': [1, 2]})\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError as error:\n" +
                "    assert 'list' in str(error)\n" +
                "try:\n" +
                "    props._plan_custom({'Bad': None})\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError as error:\n" +
                "    assert 'NoneType' in str(error)\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void ClashTestModule_ToleranceAndMaps_PureHelpers()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import clashtest\n" +
                "# _tolerance_model_units: meters / meters_per_unit, e.g. 1mm in feet\n" +
                "actual = clashtest._tolerance_model_units(0.001, 0.3048)\n" +
                "assert abs(actual - 0.00328084) < 1e-6, actual\n" +
                "assert clashtest._tolerance_model_units(1.0, 1.0) == 1.0\n" +
                "\n" +
                "# _TYPE_MAP / _STATUS_MAP: reflected live enum member names\n" +
                "assert clashtest._TYPE_MAP == " +
                "{'hard': 'Hard', 'clearance': 'Clearance', 'duplicate': 'Duplicate'}\n" +
                "assert clashtest._STATUS_MAP == {'new': 'New', 'active': 'Active', " +
                "'reviewed': 'Reviewed', 'approved': 'Approved', 'resolved': 'Resolved'}\n" +
                "\n" +
                "# _map_lookup: known keys pass through; unknown keys raise ValueError\n" +
                "# naming every valid option plus the offending key\n" +
                "assert clashtest._map_lookup(clashtest._TYPE_MAP, 'hard', 'test_type') == 'Hard'\n" +
                "try:\n" +
                "    clashtest._map_lookup(clashtest._TYPE_MAP, 'bogus', 'test_type')\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError as error:\n" +
                "    msg = str(error)\n" +
                "    assert 'test_type' in msg and 'bogus' in msg\n" +
                "    for name in ('hard', 'clearance', 'duplicate'):\n" +
                "        assert name in msg\n" +
                "try:\n" +
                "    clashtest._map_lookup(clashtest._STATUS_MAP, 'bogus', 'status')\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError as error:\n" +
                "    msg = str(error)\n" +
                "    assert 'status' in msg and 'bogus' in msg\n" +
                "    for name in ('new', 'active', 'reviewed', 'approved', 'resolved'):\n" +
                "        assert name in msg\n" +
                "\n" +
                "# _plan_edit: only non-None fields appear, mapped onto the live\n" +
                "# ClashTest property names, in a fixed (name, tolerance) order\n" +
                "assert clashtest._plan_edit({'name': None, 'tolerance_model_units': None}) == []\n" +
                "assert clashtest._plan_edit({'name': 'Foo', 'tolerance_model_units': None}) == " +
                "[('DisplayName', 'Foo')]\n" +
                "assert clashtest._plan_edit({'name': None, 'tolerance_model_units': 0.5}) == " +
                "[('Tolerance', 0.5)]\n" +
                "assert clashtest._plan_edit({'name': 'Foo', 'tolerance_model_units': 0.5}) == " +
                "[('DisplayName', 'Foo'), ('Tolerance', 0.5)]\n" +
                "# falsy-but-not-None values are honoured, not treated as absent\n" +
                "assert clashtest._plan_edit({'name': None, 'tolerance_model_units': 0.0}) == " +
                "[('Tolerance', 0.0)]\n" +
                "assert clashtest._plan_edit({'name': '', 'tolerance_model_units': None}) == " +
                "[('DisplayName', '')]\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void ClashTestModule_CreateAndSetStatus_RejectUnknownOptionsHeadlessly()
        {
            // test_type/status validation in _map_lookup fires BEFORE any lazy
            // Navisworks import, so these raise ValueError - not ImportError or
            // AttributeError from touching a nonexistent document - with zero
            // Navisworks dependency, same guard-first shape as
            // PropsModule_SetRemoveCustom_RejectEmptyTabNameHeadlessly above.
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import clashtest\n" +
                "try:\n" +
                "    clashtest.create('T', [], [], test_type='bogus')\n" +
                "    assert False, 'create should raise'\n" +
                "except ValueError as error:\n" +
                "    assert 'test_type' in str(error) and 'bogus' in str(error)\n" +
                "try:\n" +
                "    clashtest.set_status([], 'bogus')\n" +
                "    assert False, 'set_status should raise'\n" +
                "except ValueError as error:\n" +
                "    assert 'status' in str(error) and 'bogus' in str(error)\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void ClashTestModule_Delete_RejectsFolderedTestHeadlessly()
        {
            // delete()'s Parent guard is the first statement in the function, so a
            // plain python stub exercises it with zero Navisworks dependency -
            // same shape as the guard-first tests above.
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import clashtest\n" +
                "class FakeParent(object):\n" +
                "    pass\n" +
                "class FakeTest(object):\n" +
                "    Parent = FakeParent()\n" +
                "    DisplayName = 'Nested Test'\n" +
                "try:\n" +
                "    clashtest.delete(FakeTest())\n" +
                "    assert False, 'delete should raise'\n" +
                "except ValueError as error:\n" +
                "    assert 'Nested Test' in str(error) and 'folder' in str(error)\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void ExportModule_PublishPlan_SplitsAndValidatesHeadlessly()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import export\n" +
                "import datetime\n" +
                "\n" +
                "# defaults only: booleans always present, string/expiry fields omitted\n" +
                "options, props = export._publish_plan({})\n" +
                "assert options == {'ExcludeHiddenItems': False, 'EmbedXrefs': True}, options\n" +
                "assert props == {'AllowResave': True, 'DisplayOnOpen': False}, props\n" +
                "\n" +
                "# a mix of a string kwarg and a boolean kwarg splits into the two dicts\n" +
                "options, props = export._publish_plan({'keywords': 'a', 'exclude_hidden': True})\n" +
                "assert options == {'ExcludeHiddenItems': True, 'EmbedXrefs': True}, options\n" +
                "assert props == {'Keywords': 'a', 'AllowResave': True, 'DisplayOnOpen': False}, props\n" +
                "\n" +
                "# every kwarg supplied\n" +
                "expiry = datetime.datetime(2027, 1, 1)\n" +
                "options, props = export._publish_plan({\n" +
                "    'keywords': 'k', 'comments': 'c', 'published_for': 'pf', 'copyright': 'cr',\n" +
                "    'allow_resave': False, 'display_on_open': True, 'expiry': expiry,\n" +
                "    'exclude_hidden': True, 'embed_xrefs': False})\n" +
                "assert options == {'ExcludeHiddenItems': True, 'EmbedXrefs': False}, options\n" +
                "assert props == {'Keywords': 'k', 'Comments': 'c', 'PublishedFor': 'pf', " +
                "'Copyright': 'cr', 'AllowResave': False, 'DisplayOnOpen': True, " +
                "'ExpiryDate': expiry}, props\n" +
                "\n" +
                "# unknown kwarg raises naming it and the valid list\n" +
                "try:\n" +
                "    export._publish_plan({'bogus': 1})\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError as error:\n" +
                "    msg = str(error)\n" +
                "    assert 'bogus' in msg\n" +
                "    for name in ('keywords', 'comments', 'published_for', 'copyright',\n" +
                "                 'allow_resave', 'display_on_open', 'expiry',\n" +
                "                 'exclude_hidden', 'embed_xrefs'):\n" +
                "        assert name in msg\n" +
                "\n" +
                "# expiry accepts datetime only\n" +
                "try:\n" +
                "    export._publish_plan({'expiry': '2027-01-01'})\n" +
                "    assert False, 'should raise'\n" +
                "except ValueError as error:\n" +
                "    assert 'expiry' in str(error) and 'datetime' in str(error)\n" +
                "print('all tests passed')",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void Panes_Module_Compiles_And_Exposes_Its_Api()
        {
            var result = Run(@"
import pynavis.panes as panes
names = [n for n in ('show', 'hide', 'toggle', 'is_visible', 'slot_of', 'add_slots')
         if not hasattr(panes, n)]
assert not names, 'missing: %s' % names
");
            Assert.True(result.Succeeded, result.ErrorText);
        }
    }
}
