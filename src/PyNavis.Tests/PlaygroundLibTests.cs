using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure half of the Playground browser: extensions/pyNavis.extension/lib/playground.py,
    /// imported through the real IronPython engine. A small fixture catalog drives every
    /// plan, and the install and remove round trips run against a zip built in a temp
    /// folder, so nothing here touches the network or %APPDATA%.
    /// </summary>
    public class PlaygroundLibTests : IDisposable
    {
        private readonly IronPythonEngine _engine;
        private readonly string _temp;

        // Four tools: a plain button, a two-button stack, and a button that needs a hook.
        internal const string Fixture = @"
import json, os, zipfile
import playground as pg

def _tool(tid, title, tab, panel, container, hooks=(), siblings=(), status='untested', summary='', source=None):
    files = [tid + '/bundle.yaml', tid + '/script.py', tid + '/icon.png']
    return {'id': tid, 'title': title, 'kind': 'pushbutton', 'tab': tab, 'panel': panel,
            'container': container, 'summary': summary or (title + ' does a thing.'),
            'status': status, 'status_note': '', 'status_date': '2026-10-05', 'feature': 1,
            'shortcut': None, 'context': None, 'hooks': list(hooks), 'files': files,
            'icon': tid + '/icon.png', 'source': source, 'siblings': list(siblings)}

A = 'Model.tab/Select.panel/Select_Similar.pushbutton'
B = 'Model.tab/Select.panel/Quick.stack/One.pushbutton'
C = 'Model.tab/Select.panel/Quick.stack/Two.pushbutton'
D = 'Views.tab/Look.panel/Watch.pushbutton'

CATALOG = {
    'schema': 1, 'name': 'pyNavis Playground', 'version': '0.1.0', 'generated': '2026-10-09',
    'min_pynavis': '1.3.0', 'repo': 'https://example.invalid/playground',
    'download': {'zip': 'https://example.invalid/Playground.extension.zip', 'sha256': 'ab' * 32},
    'extension': 'Playground.extension',
    'shared': ['extension.yaml', 'startup.py', 'lib/namo/**', 'hooks/*.py'],
    'containers': [
        {'id': 'Model.tab', 'title': 'Model', 'kind': 'tab', 'files': ['Model.tab/bundle.yaml']},
        {'id': 'Model.tab/Select.panel', 'title': 'Select', 'kind': 'panel', 'files': ['Model.tab/Select.panel/bundle.yaml']},
        {'id': 'Model.tab/Select.panel/Quick.stack', 'title': 'Quick', 'kind': 'stack', 'files': ['Model.tab/Select.panel/Quick.stack/bundle.yaml']},
        {'id': 'Views.tab', 'title': 'Views', 'kind': 'tab', 'files': ['Views.tab/bundle.yaml']},
        {'id': 'Views.tab/Look.panel', 'title': 'Look', 'kind': 'panel', 'files': ['Views.tab/Look.panel/bundle.yaml']},
    ],
    'tools': [
        _tool(A, 'Select Similar', 'Model', 'Select', 'Model.tab/Select.panel', status='launched',
              summary='Selects every item that shares the ticked values.',
              source={'quote': 'I want to select similar things', 'url': 'https://example.invalid/ask', 'site': 'Forum', 'date': '2019-03'}),
        _tool(B, 'One', 'Model', 'Select', 'Model.tab/Select.panel/Quick.stack', siblings=[C], status='verified'),
        _tool(C, 'Two', 'Model', 'Select', 'Model.tab/Select.panel/Quick.stack', siblings=[B]),
        _tool(D, 'Watch', 'Views', 'Look', 'Views.tab/Look.panel', hooks=['selection-changed'], status='known-issue'),
    ],
}
TEXT = json.dumps(CATALOG)
cat = pg.parse_catalog(TEXT)

ZIP_NAMES = ['extension.yaml', 'startup.py', 'lib/namo/__init__.py', 'lib/namo/sub/deep.py',
             'hooks/selection-changed.py', 'hooks/README.txt', 'lib/other/x.py']
for c in CATALOG['containers']:
    ZIP_NAMES.extend(c['files'])
for t in CATALOG['tools']:
    ZIP_NAMES.extend(t['files'])

LAYOUTS = {
    'extension.yaml': 'name: Playground\nengine: ironpython\nlayout:\n  - Model\n  - Views\n',
    'Model.tab/bundle.yaml': 'layout:\n  - Select\n',
    'Model.tab/Select.panel/bundle.yaml': 'layout:\n  - Select_Similar\n  - Quick.stack\n',
    'Model.tab/Select.panel/Quick.stack/bundle.yaml': 'layout:\n  - One\n  - Two\n',
    'Views.tab/bundle.yaml': 'layout:\n  - Look\n',
    'Views.tab/Look.panel/bundle.yaml': 'layout:\n  - Watch\n',
}

def make_zip(path, prefix='Playground.extension/'):
    z = zipfile.ZipFile(path, 'w')
    try:
        for name in ZIP_NAMES:
            z.writestr(prefix + name, LAYOUTS.get(name, 'content of ' + name))
    finally:
        z.close()
    return path

def exists(root, rel):
    return os.path.exists(os.path.join(root, rel.replace('/', os.sep)))

def read(root, rel):
    with open(os.path.join(root, rel.replace('/', os.sep))) as f:
        return f.read()
";

        public PlaygroundLibTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension", "lib")));
            _engine.Initialize(config);
            _temp = Path.Combine(Path.GetTempPath(), "pg-lib-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
        }

        public void Dispose()
        {
            try { Directory.Delete(_temp, true); } catch { }
        }

        private void Run(string code)
        {
            var outw = new StringWriter();
            var request = new ScriptRequest
            {
                Code = Fixture + "\nTEMP = __temp__\n" + code + "\nprint('all tests passed')",
                Output = outw,
            };
            request.Globals["__temp__"] = _temp;
            var r = _engine.Execute(request);
            Assert.True(r.Succeeded, r.ErrorText + "\n" + outw);
            Assert.Contains("all tests passed", outw.ToString());
        }

        // ---- parse_catalog ---------------------------------------------------------

        [Fact]
        public void ParseCatalog_ReadsSchemaOne_AndFillsMissingOptionalFields()
        {
            Run(@"
assert cat['version'] == '0.1.0'
assert [t['id'] for t in cat['tools']] == [A, B, C, D]
raw = dict(CATALOG)
raw['tools'] = [{'id': A, 'title': 'Select Similar', 'files': [A + '/script.py']}]
lean = pg.parse_catalog(json.dumps(raw))
t = lean['tools'][0]
assert t['status'] == 'untested', t
assert t['siblings'] == [] and t['hooks'] == [] and t['source'] is None
assert t['summary'] == '' and t['tab'] == 'Model' and t['panel'] == 'Select', t
");
        }

        [Fact]
        public void ParseCatalog_RejectsAnotherSchema_BadJson_AndMissingParts()
        {
            Run(@"
def fails(text, needle):
    try:
        pg.parse_catalog(text)
    except pg.CatalogError as e:
        assert needle in str(e), str(e)
        return
    raise AssertionError('accepted: ' + text[:60])

raw = dict(CATALOG); raw['schema'] = 2
fails(json.dumps(raw), 'newer')
fails('{not json', 'could not be read')
fails('[1, 2]', 'could not be read')
raw = dict(CATALOG); del raw['tools']
fails(json.dumps(raw), 'tools')
raw = dict(CATALOG); raw['download'] = {'zip': 'x'}
fails(json.dumps(raw), 'sha256')
raw = dict(CATALOG); raw['tools'] = CATALOG['tools'] + [CATALOG['tools'][0]]
fails(json.dumps(raw), 'twice')
");
        }

        [Fact]
        public void ParseCatalog_RejectsPathsThatLeaveTheExtension()
        {
            Run(@"
for bad in ['../evil.py', '/abs.py', 'C:/abs.py', 'Model.tab/../../x.py', 'a\\b.py']:
    raw = json.loads(TEXT)
    raw['tools'][0]['files'] = [bad]
    try:
        pg.parse_catalog(json.dumps(raw))
    except pg.CatalogError as e:
        assert 'path' in str(e), str(e)
    else:
        raise AssertionError('accepted ' + bad)
raw = json.loads(TEXT)
raw['shared'] = ['../**']
try:
    pg.parse_catalog(json.dumps(raw))
except pg.CatalogError:
    pass
else:
    raise AssertionError('accepted a shared glob outside the extension')
");
        }

        // ---- install_plan ----------------------------------------------------------

        [Fact]
        public void InstallPlan_OneButton_TakesItsFiles_ItsContainers_AndTheSharedFiles_ButNoHooks()
        {
            Run(@"
plan = pg.install_plan(cat, [A], [])
assert plan['tools'] == [A], plan['tools']
assert plan['added'] == {}, plan['added']
assert set(plan['files']) == set(CATALOG['tools'][0]['files'] + ['Model.tab/bundle.yaml', 'Model.tab/Select.panel/bundle.yaml']), plan['files']
assert plan['shared'] == ['extension.yaml', 'startup.py', 'lib/namo/**'], plan['shared']
assert plan['hooks'] is False
");
        }

        [Fact]
        public void InstallPlan_OneStackMember_PullsInItsSibling_WithAReason()
        {
            Run(@"
plan = pg.install_plan(cat, [B], [])
assert plan['tools'] == [B, C], plan['tools']
assert list(plan['added'].keys()) == [C], plan['added']
assert 'One' in plan['added'][C] and 'stack' in plan['added'][C], plan['added'][C]
assert 'Model.tab/Select.panel/Quick.stack/bundle.yaml' in plan['files']
assert pg.sibling_note(cat, cat['tools'][1]) == 'Installs with Two', pg.sibling_note(cat, cat['tools'][1])
assert pg.sibling_note(cat, cat['tools'][0]) == ''
");
        }

        [Fact]
        public void InstallPlan_AToolWithAHook_BringsTheHooks()
        {
            Run(@"
plan = pg.install_plan(cat, [D], [])
assert plan['hooks'] is True
assert 'hooks/*.py' in plan['shared'], plan['shared']
# Hooks stay current while ANY installed tool needs one, even if the new one does not.
plan = pg.install_plan(cat, [A], [D])
assert plan['hooks'] is True and plan['tools'] == [A]
");
        }

        [Fact]
        public void InstallPlan_SkipsInstalledTools_AndUnknownIds()
        {
            Run(@"
plan = pg.install_plan(cat, [A, B, 'Nope.tab/X.panel/Y.pushbutton'], [A, B, C])
assert plan['tools'] == [], plan
assert plan['files'] == [] and plan['shared'] == []
");
        }

        [Fact]
        public void InstallPlan_ExpandedAgainstTheZip_IsTheExactEntrySet()
        {
            Run(@"
names = ['Playground.extension/' + n for n in ZIP_NAMES] + ['Playground.extension/lib/namo/../../evil.py']
entries = pg.zip_entries(pg.install_plan(cat, [D], []), names, 'Playground.extension')
want = set(['Playground.extension/' + n for n in
            ['extension.yaml', 'startup.py', 'lib/namo/__init__.py', 'lib/namo/sub/deep.py',
             'hooks/selection-changed.py', 'Views.tab/bundle.yaml', 'Views.tab/Look.panel/bundle.yaml']
            + CATALOG['tools'][3]['files']])
assert set(entries.keys()) == want, sorted(set(entries.keys()) ^ want)
assert entries['Playground.extension/startup.py'] == 'startup.py'
# Without the extension folder at the top of the zip the same plan still resolves.
bare = pg.zip_entries(pg.install_plan(cat, [A], []), ZIP_NAMES, 'Playground.extension')
assert 'hooks/selection-changed.py' not in bare and 'lib/namo/sub/deep.py' in bare
assert A + '/script.py' in bare and D + '/script.py' not in bare
");
        }

        // ---- remove_plan -----------------------------------------------------------

        [Fact]
        public void RemovePlan_OneStackMember_TakesTheStack_AndPrunesOnlyEmptyContainers()
        {
            Run(@"
plan = pg.remove_plan(cat, [B], [A, B, C, D])
assert plan['tools'] == [B, C], plan['tools']
assert 'Two' not in plan['added'][C] and 'One' in plan['added'][C], plan['added']
assert plan['containers'] == ['Model.tab/Select.panel/Quick.stack'], plan['containers']
assert set(plan['files']) == set(CATALOG['tools'][1]['files'] + CATALOG['tools'][2]['files'])
assert plan['hooks'] is False and plan['everything'] is False
");
        }

        [Fact]
        public void RemovePlan_LastHookTool_DropsTheHooks_AndTheLastToolDropsEverything()
        {
            Run(@"
plan = pg.remove_plan(cat, [D], [A, D])
assert plan['hooks'] is True and plan['everything'] is False
assert plan['containers'] == ['Views.tab/Look.panel', 'Views.tab'], plan['containers']
plan = pg.remove_plan(cat, [A], [A])
assert plan['everything'] is True
plan = pg.remove_plan(cat, [A], [B, C])
assert plan['tools'] == [], plan
");
        }

        // ---- installed.json, catalog url, sha256, filters ----------------------------

        [Fact]
        public void Installed_RoundTrips_AndAMissingOrCorruptFileReadsAsEmpty()
        {
            Run(@"
path = os.path.join(TEMP, 'state', 'installed.json')
assert pg.read_installed(path) == {'catalog_version': None, 'tools': {}}, pg.read_installed(path)
pg.write_installed(path, pg.record_install(pg.read_installed(path), cat, [A, B], '2026-10-09'))
data = pg.read_installed(path)
assert data['catalog_version'] == '0.1.0', data
assert sorted(data['tools'].keys()) == sorted([A, B])
assert data['tools'][A] == {'date': '2026-10-09', 'version': '0.1.0'}, data
data = pg.record_remove(data, [B])
assert list(data['tools'].keys()) == [A], data
with open(path, 'w') as f:
    f.write('{broken')
assert pg.read_installed(path) == {'catalog_version': None, 'tools': {}}, 'corrupt'
assert not [n for n in os.listdir(os.path.dirname(path)) if n.endswith('.tmp')]

cache = os.path.join(TEMP, 'cache')
assert pg.load_cached(cache) is None
pg.save_cached(TEXT, cache)
assert pg.load_cached(cache)['version'] == '0.1.0'
pg.save_cached('{broken', cache)
assert pg.load_cached(cache) is None
");
        }

        [Fact]
        public void CatalogUrl_DefaultsToGitHub_AndTheConfigOverrideMayBeAUrlOrAPath()
        {
            Run(@"
assert pg.catalog_url({}) == ('url', pg.DEFAULT_CATALOG_URL)
assert pg.DEFAULT_CATALOG_URL == 'https://raw.githubusercontent.com/pynavis/playground/main/catalog.json'
assert pg.catalog_url({'playground': {'catalog': '  '}}) == ('url', pg.DEFAULT_CATALOG_URL)
assert pg.catalog_url({'playground': 'junk'}) == ('url', pg.DEFAULT_CATALOG_URL)
assert pg.catalog_url({'playground': {'catalog': 'https://x.invalid/c.json'}}) == ('url', 'https://x.invalid/c.json')
assert pg.catalog_url({'playground': {'catalog': 'C:\\dev\\catalog.json'}}) == ('file', 'C:\\dev\\catalog.json')
assert pg.catalog_url({'playground': {'catalog': 'file:///C:/dev/catalog.json'}}) == ('file', 'C:\\dev\\catalog.json')
assert pg.catalog_url({'playground': {'catalog': '\\\\server\\share\\catalog.json'}}) == ('file', '\\\\server\\share\\catalog.json')
");
        }

        [Fact]
        public void CatalogOverride_IsWrittenIntoConfigJson_KeepingEveryOtherKey()
        {
            Run(@"
path = os.path.join(TEMP, 'config.json')
assert pg.read_config(path) == {}
with open(path, 'w') as f:
    f.write(json.dumps({'extensions': ['C:/x'], 'shortcuts': {'bindings': {'a': 'Ctrl+Q'}}}))
pg.save_catalog_override(path, '  C:\\dev\\catalog.json ')
data = pg.read_config(path)
assert data['playground'] == {'catalog': 'C:\\dev\\catalog.json'}, data
assert data['extensions'] == ['C:/x'] and data['shortcuts'] == {'bindings': {'a': 'Ctrl+Q'}}
assert pg.catalog_url(data) == ('file', 'C:\\dev\\catalog.json')
pg.save_catalog_override(path, '')
data = pg.read_config(path)
assert 'playground' not in data and data['extensions'] == ['C:/x'], data
with open(path, 'w') as f:
    f.write('{broken')
try:
    pg.save_catalog_override(path, 'x')
except ValueError:
    pass
else:
    raise AssertionError('overwrote an unreadable config.json')
");
        }

        [Fact]
        public void ZipSource_FollowsTheCatalog_AndAResolvesARelativeZipBesideALocalCatalog()
        {
            Run(@"
assert pg.zip_source(cat, 'url', pg.DEFAULT_CATALOG_URL) == ('url', 'https://example.invalid/Playground.extension.zip')
local = json.loads(TEXT)
local['download']['zip'] = 'dist/Playground.extension.zip'
local = pg.parse_catalog(json.dumps(local))
kind, where = pg.zip_source(local, 'file', 'C:\\dev\\pg\\catalog.json')
assert (kind, where) == ('file', 'C:\\dev\\pg\\dist\\Playground.extension.zip'), where
local['download']['zip'] = 'D:\\z\\p.zip'
assert pg.zip_source(local, 'file', 'C:\\dev\\catalog.json') == ('file', 'D:\\z\\p.zip')
assert pg.zip_cache_path(cat, 'C:\\data') == 'C:\\data\\0.1.0.zip'
odd = dict(cat); odd['version'] = '1.0/../x'
assert pg.zip_cache_path(odd, 'C:\\data') == 'C:\\data\\1.0_.._x.zip', pg.zip_cache_path(odd, 'C:\\data')
");
        }

        [Fact]
        public void WebUrl_PassesOnlyHttpAddresses_ToTheBrowser()
        {
            Run(@"
assert pg.web_url('https://example.invalid/ask') == 'https://example.invalid/ask'
assert pg.web_url(' http://example.invalid/a ') == 'http://example.invalid/a'
for bad in [None, '', 'C:/Windows/system32/calc.exe', 'file:///C:/x.exe', 'javascript:alert(1)', 'https://a b', 42]:
    assert pg.web_url(bad) is None, bad
");
        }

        [Fact]
        public void Sha256_MatchesTheKnownDigest_IgnoringCase()
        {
            Run(@"
path = os.path.join(TEMP, 'abc.bin')
with open(path, 'wb') as f:
    f.write(b'abc')
digest = 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'
assert pg.sha256_file(path) == digest
assert pg.verify_sha256(path, digest.upper())
assert not pg.verify_sha256(path, 'ab' * 32)
assert not pg.verify_sha256(path, '')
");
        }

        [Fact]
        public void FilterTools_SearchesTitleSummaryTabAndPanel_AndFiltersByStatus()
        {
            Run(@"
ids = lambda ts: [t['id'] for t in ts]
assert ids(pg.filter_tools(cat['tools'], '', 'All')) == [A, B, C, D]
assert ids(pg.filter_tools(cat['tools'], 'similar', 'All')) == [A]
assert ids(pg.filter_tools(cat['tools'], 'TICKED', 'All')) == [A]
assert ids(pg.filter_tools(cat['tools'], 'views', 'All')) == [D]
assert ids(pg.filter_tools(cat['tools'], 'look', 'All')) == [D]
assert ids(pg.filter_tools(cat['tools'], '', 'verified')) == [B]
assert ids(pg.filter_tools(cat['tools'], 'select', 'untested')) == [C]
assert pg.STATUSES == ['untested', 'launched', 'verified', 'known-issue']
");
        }

        [Fact]
        public void FilterLayout_KeepsPresentEntriesInOrder_AndLeavesTheRestOfTheYamlAlone()
        {
            Run(@"
text = 'title: Select\nlayout:\n  - Select_Similar\n  - Quick.stack\n  - gone\ntooltip: hi\n'
out = pg.filter_layout(text, ['quick.STACK', 'Select_Similar.pushbutton'])
assert out == 'title: Select\nlayout:\n  - Select_Similar\n  - Quick.stack\ntooltip: hi\n', repr(out)
assert pg.filter_layout('title: x\n', ['a']) == 'title: x\n'
assert pg.filter_layout(text, []) == 'title: Select\ntooltip: hi\n', repr(pg.filter_layout(text, []))
");
        }

        // ---- the round trip on disk -----------------------------------------------

        [Fact]
        public void InstallThenRemove_OnDisk_LeavesExactlyWhatWasAskedFor()
        {
            Run(@"
zpath = make_zip(os.path.join(TEMP, '0.1.0.zip'))
ext = os.path.join(TEMP, 'extensions', 'Playground.extension')
assert pg.installed_on_disk(cat, ext) == []

plan = pg.install_plan(cat, [A, D], [])
pg.install_files(zpath, plan, ext)
for rel in CATALOG['tools'][0]['files'] + CATALOG['tools'][3]['files'] + ['lib/namo/sub/deep.py', 'hooks/selection-changed.py', 'startup.py']:
    assert exists(ext, rel), rel
assert not exists(ext, B) and not exists(ext, 'Model.tab/Select.panel/Quick.stack')
assert not exists(ext, 'lib/other') and not exists(ext, 'hooks/README.txt')
assert pg.installed_on_disk(cat, ext) == [A, D]
# The layout lists only what is on disk, so a Reload reports no missing folders.
assert read(ext, 'Model.tab/Select.panel/bundle.yaml') == 'layout:\n  - Select_Similar\n', read(ext, 'Model.tab/Select.panel/bundle.yaml')
assert 'Views' in read(ext, 'extension.yaml')
# Nothing half-written is left beside the extension.
assert os.listdir(os.path.dirname(ext)) == ['Playground.extension'], os.listdir(os.path.dirname(ext))

# A second install adds the stack and keeps the first tool's files as they are.
with open(os.path.join(ext, A.replace('/', os.sep), 'script.py'), 'w') as f:
    f.write('tweaked')
pg.install_files(zpath, pg.install_plan(cat, [B], [A, D]), ext)
assert read(ext, A + '/script.py') == 'tweaked'
assert pg.installed_on_disk(cat, ext) == [A, B, C, D]
assert read(ext, 'Model.tab/Select.panel/bundle.yaml') == 'layout:\n  - Select_Similar\n  - Quick.stack\n'

pg.remove_files(ext, pg.remove_plan(cat, [D], [A, B, C, D]))
assert not exists(ext, 'Views.tab') and not exists(ext, 'hooks')
assert 'Views' not in read(ext, 'extension.yaml') and 'Model' in read(ext, 'extension.yaml')
pg.remove_files(ext, pg.remove_plan(cat, [C], [A, B, C]))
assert pg.installed_on_disk(cat, ext) == [A]
assert not exists(ext, 'Model.tab/Select.panel/Quick.stack')
pg.remove_files(ext, pg.remove_plan(cat, [A], [A]))
assert not os.path.exists(ext)
");
        }

        [Fact]
        public void Install_WritesExtensionYaml_WhenTheZipHasNone_AndAcceptsAZipWithoutTheTopFolder()
        {
            Run(@"
ZIP_NAMES.remove('extension.yaml')
zpath = make_zip(os.path.join(TEMP, 'bare.zip'), prefix='')
ext = os.path.join(TEMP, 'x', 'Playground.extension')
pg.install_files(zpath, pg.install_plan(cat, [A], []), ext)
assert read(ext, 'extension.yaml') == 'name: Playground\nengine: ironpython\n', read(ext, 'extension.yaml')
assert exists(ext, A + '/script.py')
");
        }

        // ---- review hardening ------------------------------------------------------

        [Fact]
        public void ParseCatalog_PinsTheExtensionFolder_SoNothingLandsOrIsDeletedElsewhere()
        {
            Run(@"
for bad in ['../../Windows', '..', 'Other.extension', 'a/Playground.extension',
            'C:\\Playground.extension', '..\\Playground.extension', '', 42]:
    raw = json.loads(TEXT)
    raw['extension'] = bad
    try:
        pg.parse_catalog(json.dumps(raw))
    except pg.CatalogError as e:
        assert 'extension' in str(e), str(e)
    else:
        raise AssertionError('accepted extension %r' % (bad,))
raw = json.loads(TEXT)
raw['extension'] = 'playground.extension'
assert pg.parse_catalog(json.dumps(raw))['extension'] == 'Playground.extension'
raw = json.loads(TEXT)
del raw['extension']
assert pg.parse_catalog(json.dumps(raw))['extension'] == 'Playground.extension'
evil = dict(cat)
evil['extension'] = '..\\..\\..'
assert pg.extension_dir(evil) == pg.extension_dir(), pg.extension_dir(evil)
assert pg.extension_dir().endswith(os.path.join('pyNavis', 'extensions', 'Playground.extension'))
");
        }

        [Fact]
        public void ParseCatalog_RejectsPathsWindowsWouldReadDifferently()
        {
            Run(@"
for bad in ['Model.tab/.. /x.py', 'Model.tab/x./y.py', 'Model.tab/x /y.py', 'Model.tab/ .. /y.py',
            'CON/x.py', 'Model.tab/nul.txt', 'Model.tab/Com1.py', 'a/b""c.py', 'a/b<c.py',
            'a/b>c.py', 'a/b|c.py', 'a/b\tc.py', 'a/*.py', 'a/b?.py']:
    raw = json.loads(TEXT)
    raw['tools'][0]['files'] = [bad]
    try:
        pg.parse_catalog(json.dumps(raw))
    except pg.CatalogError:
        pass
    else:
        raise AssertionError('accepted ' + repr(bad))
for bad in ['lib/.. /**', 'aux/*.py', 'hooks/x|y.py']:
    raw = json.loads(TEXT)
    raw['shared'] = [bad]
    try:
        pg.parse_catalog(json.dumps(raw))
    except pg.CatalogError:
        pass
    else:
        raise AssertionError('accepted shared ' + repr(bad))
# Globs stay legal in shared, and ordinary names with dots inside stay legal everywhere.
raw = json.loads(TEXT)
raw['shared'] = ['lib/namo/**', 'hooks/*.py', 'a?.txt', 'console.py', 'Model.tab/x.y.z/a.py']
pg.parse_catalog(json.dumps(raw))
");
        }

        [Fact]
        public void ZipSource_FromANetworkCatalog_IsOnlyEverAWebAddress()
        {
            Run(@"
for bad in ['\\\\evil\\share\\p.zip', '//evil/share/p.zip', 'file:///C:/p.zip', 'file://evil/s/p.zip',
            'C:\\p.zip', 'p.zip', '..\\p.zip']:
    raw = json.loads(TEXT)
    raw['download']['zip'] = bad
    c = pg.parse_catalog(json.dumps(raw))
    try:
        pg.zip_source(c, 'url', pg.DEFAULT_CATALOG_URL)
    except pg.CatalogError as e:
        assert 'web address' in str(e), str(e)
    else:
        raise AssertionError('a network catalog named a local zip: ' + bad)
");
        }

        [Fact]
        public void TextFiles_AreReadAndWrittenAsUtf8()
        {
            Run(@"
import io
cfg = os.path.join(TEMP, 'config.json')
with io.open(cfg, 'w', encoding='utf-8') as f:
    f.write(u'{""user"": ""Jos\u00e9 \u0141\u00f3d\u017a \u00dd""}')
pg.save_catalog_override(cfg, 'C:\\pg\\catalog.json')
with io.open(cfg, 'r', encoding='utf-8') as f:
    data = json.loads(f.read())
assert data['user'] == u'Jos\u00e9 \u0141\u00f3d\u017a \u00dd', repr(data['user'])
assert data['playground']['catalog'] == 'C:\\pg\\catalog.json'

title = u'title: \u0141\u00f3d\u017a \u00dd \u03b2\n'
LAYOUTS['Model.tab/Select.panel/bundle.yaml'] = title + 'layout:\n  - Select_Similar\n  - Quick.stack\n'
z = zipfile.ZipFile(os.path.join(TEMP, 'u.zip'), 'w')
try:
    for name in ZIP_NAMES:
        z.writestr('Playground.extension/' + name, LAYOUTS.get(name, 'content of ' + name).encode('utf-8'))
finally:
    z.close()
ext = os.path.join(TEMP, 'u', 'Playground.extension')
pg.install_files(os.path.join(TEMP, 'u.zip'), pg.install_plan(cat, [A], []), ext)
with open(os.path.join(ext, 'Model.tab', 'Select.panel', 'bundle.yaml'), 'rb') as f:
    raw = f.read()
assert raw == (title + 'layout:\n  - Select_Similar\n').encode('utf-8'), repr(raw)
");
        }

        [Fact]
        public void FilterLayout_ReadsListsTheWayPyNavisDoes()
        {
            Run(@"
text = 'title: X\nLayout:\n- A\n\n  # note\n  - B.pushbutton\n- C\n\nicon: y\n'
got = pg.filter_layout(text, ['B.pushbutton', 'C.stack'])
assert got == 'title: X\nLayout:\n\n  # note\n  - B.pushbutton\n- C\n\nicon: y\n', repr(got)
got = pg.filter_layout(text, [])
assert got == 'title: X\n\nicon: y\n', repr(got)
crlf = 'layout:\r\n  - A\r\n  - B\r\n'
assert pg.filter_layout(crlf, ['B']) == 'layout:\r\n  - B\r\n'
");
        }

        private static void Junction(string link, string target)
        {
            var info = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using (var p = System.Diagnostics.Process.Start(info))
            {
                p.StandardOutput.ReadToEnd();
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                Assert.True(p.ExitCode == 0 && Directory.Exists(link), "mklink /J failed: " + err);
            }
        }

        private static string Py(string path) => "r'" + path + "'";

        [Fact]
        public void LinkedFolders_AreUnlinked_NeverEmptied()
        {
            // A person who links a working copy into the extension must not lose it
            // to Install (which swaps lib/namo out) or to Remove.
            var zip = Path.Combine(_temp, "0.1.0.zip");
            var ext = Path.Combine(_temp, "extensions", "Playground.extension");
            var outsideLib = Path.Combine(_temp, "outside-lib");
            var outsideTool = Path.Combine(_temp, "outside-tool");
            var outsideExt = Path.Combine(_temp, "outside-ext");
            foreach (var d in new[] { outsideLib, outsideTool, outsideExt })
            {
                Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(d, "keep.txt"), "mine");
            }

            Run("make_zip(" + Py(zip) + ")\npg.install_files(" + Py(zip) + ", pg.install_plan(cat, [A], []), " + Py(ext) + ")\n");
            Directory.Delete(Path.Combine(ext, "lib", "namo"), true);
            Junction(Path.Combine(ext, "lib", "namo"), outsideLib);
            var toolDir = Path.Combine(ext, "Model.tab", "Select.panel", "Select_Similar.pushbutton");
            Directory.Delete(toolDir, true);
            Junction(toolDir, outsideTool);

            Run("ext = " + Py(ext) + @"
pg.install_files(" + Py(zip) + @", pg.install_plan(cat, [D], [A]), ext)
assert pg.installed_on_disk(cat, ext) == [A, D]
pg.remove_files(ext, pg.remove_plan(cat, [A, D], [A, D]))
assert not os.path.exists(ext)
");
            Assert.True(File.Exists(Path.Combine(outsideLib, "keep.txt")), "Install emptied a linked lib folder");
            Assert.True(File.Exists(Path.Combine(outsideTool, "keep.txt")), "Remove emptied a linked tool folder");

            // The whole extension linked to a working copy: removing the last tool
            // drops the link and leaves the copy alone.
            Junction(ext, outsideExt);
            Run("ext = " + Py(ext) + @"
pg.remove_files(ext, {'tools': [A], 'added': {}, 'files': [], 'containers': [], 'hooks': False, 'everything': True})
assert not os.path.exists(ext)
");
            Assert.True(File.Exists(Path.Combine(outsideExt, "keep.txt")), "Remove emptied a linked extension");
        }
    }

    /// <summary>
    /// The Playground window from the shipped script.py, built on an STA thread and
    /// filled from the fixture catalog without being shown: catches a misnamed WPF
    /// member or a broken row before anyone clicks the button in Navisworks.
    /// </summary>
    public class PlaygroundWindowTests
    {
        [Fact]
        public void Browser_BuildsRowsFromACatalog_FiltersThem_AndCountsTheTicks()
        {
            Exception failure = null;
            string error = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var engine = new IronPythonEngine();
                    var config = new EngineConfig();
                    var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
                    if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
                    config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
                    var ext = Path.GetFullPath(Path.Combine(
                        PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension"));
                    config.SearchPaths.Add(Path.Combine(ext, "lib"));
                    config.SearchPaths.Add(Path.Combine(ext, "pyNavis.tab", "pyNavis.panel", "Playground.pushbutton"));
                    engine.Initialize(config);

                    var outw = new StringWriter();
                    var r = engine.Execute(new ScriptRequest
                    {
                        Code = PlaygroundLibTests.Fixture + @"
import script as ui
from System.Windows import Visibility
b = ui.Browser()
cat['min_pynavis'] = '0.1'
b._show(cat, 'file', 'C:/nowhere/catalog.json', '')
assert sorted(b.rows.keys()) == sorted([A, B, C, D])
shown = lambda: sorted(t for t, (row, check) in b.rows.items() if row.Visibility == Visibility.Visible)
assert shown() == sorted([A, B, C, D])
b.search.Text = 'similar'
assert shown() == [A], shown()
b.search.Text = ''
b.filter.SelectedIndex = 4
assert shown() == [D], shown()
b.filter.SelectedIndex = 0
assert not b.install_button.IsEnabled and not b.remove_button.IsEnabled
b.rows[B][1].IsChecked = True
assert b.count.Text == '1 selected, 2 to install', b.count.Text
assert b.install_button.IsEnabled
assert 'experimental' in b.status.Text, b.status.Text
cat['min_pynavis'] = '99.0'
b._show(cat, 'file', 'C:/nowhere/catalog.json', '')
assert 'needs pyNavis 99.0' in b.status.Text, b.status.Text
assert not b.install_button.IsEnabled
print('all tests passed')
",
                        Output = outw,
                    });
                    error = r.Succeeded ? null : r.ErrorText + "\n" + outw;
                    if (r.Succeeded && !outw.ToString().Contains("all tests passed")) error = outw.ToString();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA action failed: " + failure);
            Assert.True(error == null, error);
        }
    }
}
