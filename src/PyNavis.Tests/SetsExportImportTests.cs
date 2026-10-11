using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// sets.export_all feeding sets.import_dict, for folder and set names that
    /// carry a slash of their own. find() and create_folder() split a path on
    /// every unescaped '/', so both halves have to write such a name as 'A\/B'
    /// or it comes back as two levels, and replace=True misses the set it was
    /// meant to update and creates a duplicate beside it.
    ///
    /// Runs the real snapshot, find, export_all and import_dict over a python
    /// stand-in for the selection-sets tree, injected as pynavis._api the same
    /// way SetsCompileSearchTests does. Only to_dict (the Search reverse-mapping)
    /// and the two writers, update and create, are swapped for recorders.
    /// </summary>
    public class SetsExportImportTests
    {
        private readonly IronPythonEngine _engine;

        public SetsExportImportTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            _engine.Initialize(config);
        }

        private const string FakeTree =
            "import sys, types\n" +
            "from System import Guid\n" +
            "class FolderItem(object):\n" +
            "    def __init__(self, name, children=()):\n" +
            "        self.DisplayName = name\n" +
            "        self.Children = list(children)\n" +
            "        self.Guid = Guid.NewGuid()\n" +
            "class SelectionSet(object):\n" +
            "    HasSearch = True\n" +
            "    def __init__(self, name):\n" +
            "        self.DisplayName = name\n" +
            "        self.Guid = Guid.NewGuid()\n" +
            "class FakeApi(object):\n" +
            "    FolderItem = FolderItem\n" +
            "    SelectionSet = SelectionSet\n" +
            "class FakeSets(object):\n" +
            "    def __init__(self, children):\n" +
            "        self.RootItem = FolderItem('', children)\n" +
            "    def ResolveGuid(self, guid):\n" +
            "        def walk(children):\n" +
            "            for item in children:\n" +
            "                if str(item.Guid) == str(guid):\n" +
            "                    return item\n" +
            "                if isinstance(item, FolderItem):\n" +
            "                    found = walk(item.Children)\n" +
            "                    if found is not None:\n" +
            "                        return found\n" +
            "            return None\n" +
            "        return walk(self.RootItem.Children)\n" +
            "class FakeDoc(object):\n" +
            "    IsActiveTransaction = True\n" +   // transaction() leaves the document alone
            "    def __init__(self, children):\n" +
            "        self.SelectionSets = FakeSets(children)\n" +
            "module = types.ModuleType('pynavis._api')\n" +
            "module.Api = FakeApi\n" +
            "module.Application = None\n" +
            "sys.modules['pynavis._api'] = module\n" +
            "import pynavis\n" +
            "pynavis._api = module\n" +
            "from pynavis import sets\n" +
            "QUERY = {'or': [{'and': [{'category': 'Item', 'prop': 'Type', 'op': 'equals', 'value': 'Duct'}]}]}\n" +
            "sets.to_dict = lambda item, doc=None: QUERY\n" +
            "UPDATED, CREATED = [], []\n" +
            "sets.update = lambda item, query, doc=None: UPDATED.append(item)\n" +
            "sets.create = lambda name, query, folder=None, doc=None: CREATED.append((name, folder))\n" +
            "duct = SelectionSet('Duct A/B')\n" +
            "pipe = SelectionSet('Pipe C/D')\n" +
            "leaf = SelectionSet('Leaf')\n" +
            "doc = FakeDoc([\n" +
            "    FolderItem('Level 1/2', [duct]),\n" +
            "    pipe,\n" +
            "    FolderItem('Plain', [FolderItem('Sub/Way', [leaf])]),\n" +
            "])\n";

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = FakeTree + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void ExportAll_EscapesASlashInsideAFolderName()
        {
            Run(
                "data = sets.export_all(doc)\n" +
                "folders = dict((e['name'], e['folder']) for e in data['sets'])\n" +
                "assert sorted(folders) == ['Duct A/B', 'Leaf', 'Pipe C/D'], folders\n" +
                "assert sets._split_path(folders['Duct A/B']) == ['Level 1/2'], folders\n" +
                "assert sets._split_path(folders['Leaf']) == ['Plain', 'Sub/Way'], folders\n" +
                "assert folders['Pipe C/D'] is None, folders\n");
        }

        [Fact]
        public void ImportDict_Replace_FindsEverySetItExported_EvenWithASlashInItsName()
        {
            Run(
                "created, errors = sets.import_dict(sets.export_all(doc), doc, replace=True)\n" +
                "assert errors == [], errors\n" +
                "assert created == 3, created\n" +
                "assert CREATED == [], 'replace=True created duplicates: %r' % CREATED\n" +
                "assert UPDATED == [duct, pipe, leaf], [x.DisplayName for x in UPDATED]\n");
        }

        [Fact]
        public void ImportDict_Create_GetsAFolderPathThatSplitsBackToTheSameFolders()
        {
            Run(
                "sets.import_dict(sets.export_all(doc), doc)\n" +
                "folders = dict(CREATED)\n" +
                "assert sets._split_path(folders['Duct A/B']) == ['Level 1/2'], folders\n" +
                "assert sets._split_path(folders['Leaf']) == ['Plain', 'Sub/Way'], folders\n" +
                "assert folders['Pipe C/D'] is None, folders\n" +
                "assert UPDATED == []\n");
        }
    }
}
