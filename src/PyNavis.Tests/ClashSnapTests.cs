using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure snapshot memos (pynavis._clashsnap) through the real IronPython
    /// engine. These exist so clash.py's caching is testable at all: clash.py
    /// itself cannot be imported outside a Navisworks session.
    /// </summary>
    public class ClashSnapTests
    {
        private readonly IronPythonEngine _engine;

        public ClashSnapTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            _engine.Initialize(config);
        }

        private ExecResult Run(string code, TextWriter output = null)
        {
            return _engine.Execute(new ScriptRequest { Code = code, Output = output });
        }

        [Fact]
        public void Memo_ReadsOncePerKey_AndReplaysAfterThat()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import _clashsnap\n" +
                "reads = []\n" +
                "def read(subject):\n" +
                "    reads.append(subject)\n" +
                "    return subject * 10\n" +
                "memo = _clashsnap.Memo(lambda s: s, read)\n" +
                "got = [memo.get(1), memo.get(2), memo.get(1), memo.get(1)]\n" +
                "print('got=%s' % got)\n" +
                "print('reads=%s' % reads)\n" +
                "print('hits=%d misses=%d' % (memo.hits, memo.misses))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("got=[10, 20, 10, 10]", outw.ToString());
            Assert.Contains("reads=[1, 2]", outw.ToString());
            Assert.Contains("hits=2 misses=2", outw.ToString());
        }

        [Fact]
        public void Memo_UnkeyableSubjects_AlwaysFallThrough_AndNeverPoisonTheCache()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import _clashsnap\n" +
                "calls = [0]\n" +
                "def read(subject):\n" +
                "    calls[0] += 1\n" +
                "    return 'v%d' % calls[0]\n" +
                // key_of returns None for anything falsy: an unknown element.
                "memo = _clashsnap.Memo(lambda s: s or None, read)\n" +
                "got = [memo.get(0), memo.get(0), memo.get('k'), memo.get('k')]\n" +
                "print('got=%s' % got)\n" +
                "print('calls=%d' % calls[0])",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            // The two unkeyable reads are distinct; the keyed one is shared.
            Assert.Contains("got=['v1', 'v2', 'v3', 'v3']", outw.ToString());
            Assert.Contains("calls=3", outw.ToString());
        }

        [Fact]
        public void CellKey_QuantisesToTheCellEdge_AndHandlesNegativesAndMisses()
        {
            var outw = new StringWriter();
            var r = Run(
                "from pynavis import _clashsnap\n" +
                "k = _clashsnap.cell_key\n" +
                "print('same=%s' % (k((0.1, 0.2, 0.3), 1.0) == k((0.9, 0.4, 0.8), 1.0)))\n" +
                "print('next=%s' % (k((0.1, 0.0, 0.0), 1.0) == k((1.1, 0.0, 0.0), 1.0)))\n" +
                "print('neg=%s' % (k((-0.5, 0.0, 0.0), 1.0),))\n" +
                "print('nocentre=%s' % (k(None, 1.0),))\n" +
                "print('nosize=%s' % (k((1.0, 1.0, 1.0), 0),))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("same=True", outw.ToString());
            Assert.Contains("next=False", outw.ToString());
            Assert.Contains("neg=(-1, 0, 0)", outw.ToString());
            Assert.Contains("nocentre=None", outw.ToString());
            Assert.Contains("nosize=None", outw.ToString());
        }
    }
}
