using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Pure grouping engine (pynavis.clashgroup) through the real IronPython
    /// engine: rule buckets, chaining, proximity clustering, smart side-pick,
    /// naming, and the never-drop-a-clash invariant.
    /// </summary>
    public class ClashGroupTests
    {
        private readonly IronPythonEngine _engine;

        // Shared fixture helper: make(1, level='L1', ...) builds a snapshot dict
        // with every field defaulted, and check(plan, results) asserts the
        // engine's core invariant - every candidate lands exactly once.
        private const string Fixture =
            "from pynavis import clashgroup\n" +
            "def make(id, **kw):\n" +
            "    r = {'id': id, 'name': 'Clash%d' % id, 'center': None,\n" +
            "         'item_a': 'a%d' % id, 'item_b': 'b%d' % id,\n" +
            "         'item_a_name': 'ItemA%d' % id, 'item_b_name': 'ItemB%d' % id,\n" +
            "         'level': None, 'grid': None, 'model_a': '', 'model_b': '',\n" +
            "         'status': 'New', 'assigned': '', 'group': None}\n" +
            "    r.update(kw)\n" +
            "    return r\n" +
            "def ids_of(plan):\n" +
            "    out = []\n" +
            "    for g in plan['groups']: out.extend(g['ids'])\n" +
            "    return sorted(out + plan['ungrouped_ids'])\n" +
            "def check(plan, results):\n" +
            "    grouped = [i for g in plan['groups'] for i in g['ids']]\n" +
            "    assert len(grouped) == len(set(grouped)), 'clash in two groups'\n" +
            "    candidates = [r['id'] for r in results if not r['group']]\n" +
            "    assert ids_of(plan) == sorted(candidates), (ids_of(plan), candidates)\n";

        public ClashGroupTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            _engine.Initialize(config);
        }

        private void Run(string code)
        {
            var r = _engine.Execute(new ScriptRequest { Code = Fixture + code });
            Assert.True(r.Succeeded, r.ErrorText);
        }

        [Fact]
        public void LevelRule_Buckets_AndMissingDataGetsNamedGroup_NotDropped()
        {
            Run(
                "rs = [make(1, level='L1'), make(2, level='L1'), make(3, level='L2'), make(4)]\n" +
                "p = clashgroup.plan(rs, ['level'])\n" +
                "check(p, rs)\n" +
                "names = [g['name'] for g in p['groups']]\n" +
                "assert names == ['L1', 'L2', 'No level'], names\n" +
                "assert p['groups'][0]['ids'] == [1, 2]\n");
        }

        [Fact]
        public void ChainedRules_Subdivide_AndComposeNames()
        {
            Run(
                "rs = [make(1, level='L1', grid='A-1'), make(2, level='L1', grid='A-1'),\n" +
                "      make(3, level='L1', grid='B-2'), make(4, level='L2', grid='A-1')]\n" +
                "p = clashgroup.plan(rs, ['level', 'grid'])\n" +
                "check(p, rs)\n" +
                "names = [g['name'] for g in p['groups']]\n" +
                "assert names == ['L1 / A-1', 'L1 / B-2', 'L2 / A-1'], names\n");
        }

        [Fact]
        public void KeepExisting_LeavesGroupedResultsAlone_AndCountsThem()
        {
            Run(
                "rs = [make(1, level='L1'), make(2, level='L1', group='Manual'), make(3, level='L1')]\n" +
                "p = clashgroup.plan(rs, ['level'])\n" +
                "check(p, rs)\n" +
                "assert p['skipped_existing'] == 1\n" +
                "assert p['groups'][0]['ids'] == [1, 3]\n" +
                "p2 = clashgroup.plan(rs, ['level'], keep_existing=False)\n" +
                "assert p2['skipped_existing'] == 0\n" +
                "assert p2['groups'][0]['ids'] == [1, 2, 3]\n");
        }

        [Fact]
        public void Proximity_ClustersWithinTolerance_AndNamesByDominantGrid()
        {
            Run(
                "rs = [make(1, center=(0, 0, 0), grid='A-1'), make(2, center=(3, 0, 0), grid='A-1'),\n" +
                "      make(3, center=(100, 0, 0), grid='J-9'), make(4)]\n" +
                "p = clashgroup.plan(rs, ['proximity'], tolerance=5.0)\n" +
                "check(p, rs)\n" +
                "names = [g['name'] for g in p['groups']]\n" +
                "assert names == ['Area A-1', 'Area J-9', 'Unlocated'], names\n" +
                "assert p['groups'][0]['ids'] == [1, 2]\n" +
                "tight = clashgroup.plan(rs, ['proximity'], tolerance=1.0)\n" +
                "assert len(tight['groups']) == 4, tight['groups']\n");
        }

        [Fact]
        public void ProximityChain_ClustersInsideEachParentPartition()
        {
            Run(
                "rs = [make(1, level='L1', center=(0, 0, 0)), make(2, level='L1', center=(1, 0, 0)),\n" +
                "      make(3, level='L2', center=(0.5, 0, 0))]\n" +
                "p = clashgroup.plan(rs, ['level', 'proximity'], tolerance=5.0)\n" +
                "check(p, rs)\n" +
                "assert len(p['groups']) == 2, p['groups']\n" +
                "assert p['groups'][0]['ids'] == [1, 2]\n" +
                "assert p['groups'][1]['ids'] == [3]\n");
        }

        [Fact]
        public void RootCauseRule_GroupsBySharedElement_FallsBackToNameKey()
        {
            Run(
                "rs = [make(1, item_a='pipe9', item_a_name='Pipe 9'),\n" +
                "      make(2, item_a='pipe9', item_a_name='Pipe 9'),\n" +
                "      make(3, item_a='', item_a_name='Duct 4'),\n" +
                "      make(4, item_a='', item_a_name='Duct 4'),\n" +
                "      make(5, item_a='', item_a_name='')]\n" +
                "p = clashgroup.plan(rs, ['item_a'])\n" +
                "check(p, rs)\n" +
                "names = [g['name'] for g in p['groups']]\n" +
                "assert names == ['Pipe 9', 'Duct 4', 'Unknown element'], names\n");
        }

        [Fact]
        public void SmartPlan_PicksCompressingSide_ClustersLeftovers_Explains()
        {
            Run(
                // Side B: one beam causes 1,2,3 (compresses); side A all distinct.
                "rs = [make(1, item_b='beam1', item_b_name='Beam 1', model_b='STR.rvt'),\n" +
                "      make(2, item_b='beam1', item_b_name='Beam 1', model_b='STR.rvt'),\n" +
                "      make(3, item_b='beam1', item_b_name='Beam 1', model_b='STR.rvt'),\n" +
                "      make(4, item_b='b4', center=(0, 0, 0), grid='C-2', model_b='STR.rvt'),\n" +
                "      make(5, item_b='b5', center=(1, 0, 0), grid='C-2', model_b='STR.rvt'),\n" +
                "      make(6, item_b='b6', center=(500, 0, 0), model_b='STR.rvt')]\n" +
                "p = clashgroup.smart_plan(rs, tolerance=5.0)\n" +
                "check(p, rs)\n" +
                "names = [g['name'] for g in p['groups']]\n" +
                "assert names == ['Beam 1', 'Area C-2'], names\n" +
                "assert p['ungrouped_ids'] == [6]\n" +
                "assert 'B (STR.rvt)' in p['explanation'], p['explanation']\n");
        }

        [Fact]
        public void ModelRule_PairsAndDedupesFileNames()
        {
            Run(
                "rs = [make(1, model_a='MEP.rvt', model_b='STR.rvt'),\n" +
                "      make(2, model_a='STR.rvt', model_b='MEP.rvt'),\n" +
                "      make(3, model_a='ARC.rvt', model_b='ARC.rvt'),\n" +
                "      make(4)]\n" +
                "p = clashgroup.plan(rs, ['model'])\n" +
                "check(p, rs)\n" +
                "names = [g['name'] for g in p['groups']]\n" +
                "assert names == ['MEP.rvt vs STR.rvt', 'ARC.rvt', 'Unknown model'], names\n");
        }

        [Fact]
        public void StatusAndAssigned_BucketWithFriendlyFallbacks()
        {
            Run(
                "rs = [make(1, status='Active'), make(2, status=''), make(3, assigned='Ana')]\n" +
                "p = clashgroup.plan(rs, ['status'])\n" +
                "check(p, rs)\n" +
                "assert sorted(g['name'] for g in p['groups']) == ['Active', 'New', 'No status']\n" +
                "p2 = clashgroup.plan(rs, ['assigned'])\n" +
                "assert [g['name'] for g in p2['groups']] == ['Unassigned', 'Ana']\n");
        }

        [Fact]
        public void DuplicateGroupNames_GetNumericSuffixes()
        {
            Run(
                // Two distinct elements sharing a display name must stay two
                // groups with distinguishable names.
                "rs = [make(1, item_a='x1', item_a_name='Pipe'), make(2, item_a='x1', item_a_name='Pipe'),\n" +
                "      make(3, item_a='x2', item_a_name='Pipe'), make(4, item_a='x2', item_a_name='Pipe')]\n" +
                "p = clashgroup.plan(rs, ['item_a'])\n" +
                "check(p, rs)\n" +
                "assert [g['name'] for g in p['groups']] == ['Pipe', 'Pipe (2)'], p['groups']\n");
        }

        [Fact]
        public void NoRules_GroupsNothing_AndSaysSo()
        {
            Run(
                "rs = [make(1), make(2)]\n" +
                "p = clashgroup.plan(rs, [])\n" +
                "assert p['groups'] == []\n" +
                "assert p['ungrouped_ids'] == [1, 2]\n" +
                "assert 'No rules' in p['explanation']\n");
        }

        [Fact]
        public void RulesCatalog_ExposesAllNineChoices()
        {
            Run(
                "ids = [rid for rid, label in clashgroup.RULES]\n" +
                "assert ids == ['item', 'item_a', 'item_b', 'proximity', 'level',\n" +
                "               'grid', 'model', 'status', 'assigned'], ids\n" +
                "assert all(label for rid, label in clashgroup.RULES)\n");
        }

        [Fact]
        public void LargeFlatInput_ClustersFast_NoQuadraticBlowup()
        {
            Run(
                // 4,000 points on a line, alternating near/far: pairs cluster.
                "rs = [make(i, center=(i * 10 + (i % 2), 0.0, 0.0)) for i in range(4000)]\n" +
                "import time\n" +
                "t0 = time.time()\n" +
                "p = clashgroup.plan(rs, ['proximity'], tolerance=2.0)\n" +
                "elapsed = time.time() - t0\n" +
                "check(p, rs)\n" +
                "assert elapsed < 10.0, 'too slow: %.1fs' % elapsed\n");
        }
    }
}
