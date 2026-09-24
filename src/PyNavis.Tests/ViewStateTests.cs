using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure half of pynavis.viewstate: kind labels, store validation, the
    /// per-document JSON storage, and the toast descriptions. The API half
    /// (clip planes, hidden walk, override read) only runs inside Navisworks
    /// and is covered by the smoke checklist.
    /// </summary>
    public class ViewStateTests : IDisposable
    {
        private readonly IronPythonEngine _engine;
        private readonly string _root;

        public ViewStateTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            _engine.Initialize(config);

            _root = Path.Combine(Path.GetTempPath(), "pynavis-viewstate-tests-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private string Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest { Code = code, Output = outw });
            Assert.True(r.Succeeded, r.ErrorText);
            return outw.ToString();
        }

        /// <summary>Python preamble binding the temp storage root.</summary>
        private string Preamble =>
            "from pynavis import viewstate\n" +
            "root = r'" + _root + "'\n";

        [Fact]
        public void Kinds_AndLabels_RoundTrip()
        {
            var text = Run(Preamble +
                "print('kinds=%s' % ','.join(viewstate.KINDS))\n" +
                "print('labels=%s' % '|'.join(viewstate.label_for(k) for k in viewstate.KINDS))\n" +
                "print('back=%s' % ','.join(viewstate.kind_for(viewstate.label_for(k)) for k in viewstate.KINDS))\n" +
                "print('unknown=%s' % viewstate.kind_for('Crop Region'))");

            Assert.Contains("kinds=section,hidden,overrides", text);
            Assert.Contains("labels=Section State|Hidden Items|Appearance Overrides", text);
            Assert.Contains("back=section,hidden,overrides", text);
            Assert.Contains("unknown=None", text);
        }

        [Fact]
        public void SaveKind_ThenLoadStore_RoundTrips_PerDocument()
        {
            var text = Run(Preamble +
                "viewstate.save_kind(r'C:\\models\\plant.nwf', 'hidden', {'items': [1, 2, 3]}, root=root)\n" +
                "viewstate.save_kind(r'C:\\models\\plant.nwf', 'section', {'mode': 'Planes', 'planes': []}, root=root)\n" +
                "viewstate.save_kind(r'C:\\models\\other.nwf', 'hidden', {'items': []}, root=root)\n" +
                "store = viewstate.load_store(r'C:\\models\\plant.nwf', root=root)\n" +
                "print('items=%s' % store['hidden']['data']['items'])\n" +
                "print('kinds=%s' % ','.join(sorted(store.keys())))\n" +
                "other = viewstate.load_store(r'C:\\models\\other.nwf', root=root)\n" +
                "print('other=%s' % ','.join(sorted(other.keys())))");

            Assert.Contains("items=[1, 2, 3]", text);
            Assert.Contains("kinds=hidden,section", text);
            Assert.Contains("other=hidden", text);
        }

        [Fact]
        public void SaveKind_StampsTheSavedTime()
        {
            var text = Run(Preamble +
                "viewstate.save_kind('doc.nwd', 'hidden', {'items': []}, root=root)\n" +
                "store = viewstate.load_store('doc.nwd', root=root)\n" +
                "print('stamped=%s' % bool(store['hidden'].get('saved')))");

            Assert.Contains("stamped=True", text);
        }

        [Fact]
        public void LoadStore_MissingOrCorrupt_YieldsEmpty()
        {
            Directory.CreateDirectory(_root);
            var text = Run(Preamble +
                "print('missing=%s' % viewstate.load_store('doc.nwd', root=root))\n" +
                "path = viewstate.store_path('doc.nwd', root=root)\n" +
                "open(path, 'w').write('{not json')\n" +
                "print('corrupt=%s' % viewstate.load_store('doc.nwd', root=root))");

            Assert.Contains("missing={}", text);
            Assert.Contains("corrupt={}", text);
        }

        [Fact]
        public void Available_ReturnsCanonicalOrder_AndSkipsMalformedEntries()
        {
            var text = Run(Preamble +
                "store = {\n" +
                "  'overrides': {'version': 1, 'saved': 't', 'data': {'entries': []}},\n" +
                "  'section': {'version': 1, 'saved': 't', 'data': {'mode': 'Planes'}},\n" +
                "  'hidden': 'not-a-dict',\n" +
                "  'unknown-kind': {'version': 1, 'saved': 't', 'data': {}},\n" +
                "  'future': {'version': 99, 'saved': 't', 'data': {}},\n" +
                "}\n" +
                "print('avail=%s' % ','.join(viewstate.available(store)))\n" +
                "print('empty=%s' % viewstate.available({}))");

            Assert.Contains("avail=section,overrides", text);
            Assert.Contains("empty=[]", text);
        }

        [Fact]
        public void Available_SkipsEntriesFromAnUnsupportedVersion()
        {
            var text = Run(Preamble +
                "store = {'hidden': {'version': 2, 'saved': 't', 'data': {'items': []}}}\n" +
                "print('avail=%s' % viewstate.available(store))");

            Assert.Contains("avail=[]", text);
        }

        [Fact]
        public void RangeOk_RejectsInvertedAndMalformedBoxes()
        {
            // Navisworks stores "never set" as an inverted box (min > max) and
            // rejects setting one back; both copy and paste must treat it as
            // "no range" instead of crashing the paste (field bug).
            var text = Run(Preamble +
                "print('good=%s' % viewstate.range_ok([[0, 0, 0], [1, 2, 3]]))\n" +
                "print('flat=%s' % viewstate.range_ok([[0, 0, 0], [1, 0, 3]]))\n" +
                "print('inverted=%s' % viewstate.range_ok([[1e300, 1e300, 1e300], [-1e300, -1e300, -1e300]]))\n" +
                "print('one_axis=%s' % viewstate.range_ok([[0, 5, 0], [1, 2, 3]]))\n" +
                "print('none=%s' % viewstate.range_ok(None))\n" +
                "print('short=%s' % viewstate.range_ok([[0, 0], [1, 1]]))\n" +
                "print('junk=%s' % viewstate.range_ok('boxes'))");

            Assert.Contains("good=True", text);
            Assert.Contains("flat=True", text);
            Assert.Contains("inverted=False", text);
            Assert.Contains("one_axis=False", text);
            Assert.Contains("none=False", text);
            Assert.Contains("short=False", text);
            Assert.Contains("junk=False", text);
        }

        [Fact]
        public void Describe_CountsWhatEachKindCarries()
        {
            var text = Run(Preamble +
                "print('planes=' + viewstate.describe('section', {'mode': 'Planes', 'planes': [1, 2, 3]}))\n" +
                "print('box=' + viewstate.describe('section', {'mode': 'Box', 'planes': []}))\n" +
                "print('off=' + viewstate.describe('section', {'mode': 'Planes', 'planes': []}))\n" +
                "print('disabled=' + viewstate.describe('section', {'enabled': False, 'mode': 'Planes', 'planes': [1, 2]}))\n" +
                "print('active=' + viewstate.describe('section', {'enabled': True, 'mode': 'Planes', 'planes': [1, 2, 3, 4, 5, 6], 'active': 2}))\n" +
                "print('hid=' + viewstate.describe('hidden', {'items': [1, 2]}))\n" +
                "print('hid1=' + viewstate.describe('hidden', {'items': [1]}))\n" +
                "print('hid0=' + viewstate.describe('hidden', {'items': []}))\n" +
                // Overrides are an opaque Navisworks snapshot now, nothing to count.
                "print('ovr=%r' % viewstate.describe('overrides', {}))");

            // Details are user-facing toast text: sentence case, never lowercase.
            Assert.Contains("planes=3 planes", text);
            Assert.Contains("box=Section box", text);
            Assert.Contains("off=Sectioning off", text);
            Assert.Contains("disabled=Sectioning off", text);
            Assert.Contains("active=2 planes", text);
            Assert.Contains("hid=2 hidden items", text);
            Assert.Contains("hid1=1 hidden item", text);
            Assert.Contains("hid0=Everything visible", text);
            Assert.Contains("ovr=''", text);
        }
    }
}
