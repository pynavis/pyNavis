using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure tree-layout planner (pynavis._clashapply) through the real
    /// IronPython engine, following ClashGroupTests.
    ///
    /// These exist because apply_plan used to hang Navisworks: it made one live
    /// DocumentClashTests edit per group and per clash, and those edits cost
    /// 170ms to 3000ms each no matter how small the change, so a 350,000-clash
    /// job was about 4.6 days. The fix rebuilds the tree offline and commits
    /// once, which makes "what should the tree become" a pure decision - and
    /// this is where that decision is pinned, because clash.py itself cannot be
    /// imported outside a Navisworks session.
    ///
    /// The invariant every test leans on: each leaf id in the input tree comes
    /// back exactly once. A rebuild that breaks it loses or duplicates clashes,
    /// which is far worse than the hang it replaced.
    /// </summary>
    public class ClashApplyTests
    {
        private readonly IronPythonEngine _engine;

        private const string Fixture =
            "from pynavis import _clashapply\n" +
            "def plan_of(*groups):\n" +
            "    return {'groups': [{'name': n, 'ids': list(i)} for n, i in groups]}\n" +
            "def ids_in(tree):\n" +
            "    out = []\n" +
            "    for node in tree:\n" +
            "        if node[0] == 'leaf':\n" +
            "            out.append(node[1])\n" +
            "        else:\n" +
            "            out.extend(node[2])\n" +
            "    return sorted(out)\n" +
            "def check(before, after):\n" +
            "    a, b = ids_in(before), ids_in(after)\n" +
            "    assert a == b, ('ids changed', a, b)\n" +
            "    assert len(b) == len(set(b)), ('duplicate id', b)\n";

        public ClashApplyTests()
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
        public void KeepExisting_PassesExistingGroupsThrough_AndAppendsNewGroups()
        {
            Run(
                "tree = [('group', 7, [0, 1]), ('leaf', 2), ('leaf', 3), ('leaf', 4)]\n" +
                "plan = plan_of(('Pump A', [2, 3]))\n" +
                "out = _clashapply.layout(tree, plan, keep_existing=True)\n" +
                "check(tree, out)\n" +
                // The existing group is untouched and still identified by handle,
                // the unclaimed root leaf stays put, the new group lands last.
                "assert out == [('group', 7, [0, 1]), ('leaf', 4),\n" +
                "               ('new', 'Pump A', [2, 3])], out\n");
        }

        [Fact]
        public void KeepExisting_IdentifiesGroupsByHandle_NotDisplayName()
        {
            // Sibling result groups are routinely named identically (a real model
            // had three called 'North Tower Area C LEVEL 2'), so the handle is
            // what must come back, distinct per group.
            Run(
                "tree = [('group', 0, [0]), ('group', 1, [1]), ('group', 2, [2]),\n" +
                "        ('leaf', 3), ('leaf', 4)]\n" +
                "plan = plan_of(('New', [3, 4]))\n" +
                "out = _clashapply.layout(tree, plan, keep_existing=True)\n" +
                "check(tree, out)\n" +
                "assert [n[1] for n in out if n[0] == 'group'] == [0, 1, 2], out\n");
        }

        [Fact]
        public void FlattenMode_DissolvesEveryExistingGroup_LeftoversGoToRoot()
        {
            Run(
                "tree = [('group', 'g1', [0, 1]), ('group', 'g2', [2]), ('leaf', 3)]\n" +
                "plan = plan_of(('Beam 12', [0, 2]))\n" +
                "out = _clashapply.layout(tree, plan, keep_existing=False)\n" +
                "check(tree, out)\n" +
                "assert not [n for n in out if n[0] == 'group'], out\n" +
                // 3 was already loose and keeps its place; 1 lost its group and
                // falls to the end.
                "assert out == [('leaf', 3), ('new', 'Beam 12', [0, 2]),\n" +
                "               ('leaf', 1)], out\n");
        }

        [Fact]
        public void FlattenMode_WithNoPlanGroups_FlattensToBareLeaves()
        {
            Run(
                "tree = [('group', 'g1', [0, 1]), ('leaf', 2)]\n" +
                "out = _clashapply.layout(tree, plan_of(), keep_existing=False)\n" +
                "check(tree, out)\n" +
                "assert out == [('leaf', 2), ('leaf', 0), ('leaf', 1)], out\n");
        }

        [Fact]
        public void PartiallyClaimedGroup_KeepsOnlySurvivors_WhenKeepingExisting()
        {
            // Unreachable through either supported mode (a keep_existing plan
            // never claims a grouped clash), so this pins the defensive path
            // rather than a routine one.
            Run(
                "tree = [('group', 5, [0, 1, 2])]\n" +
                "out = _clashapply.layout(tree, plan_of(('X', [1])), keep_existing=True)\n" +
                "check(tree, out)\n" +
                "assert out == [('group', 5, [0, 2]), ('new', 'X', [1])], out\n");
        }

        [Fact]
        public void GroupEmptiedByThePlan_Disappears()
        {
            Run(
                "tree = [('group', 5, [0, 1]), ('leaf', 2)]\n" +
                "out = _clashapply.layout(tree, plan_of(('X', [0, 1])), keep_existing=True)\n" +
                "check(tree, out)\n" +
                "assert out == [('leaf', 2), ('new', 'X', [0, 1])], out\n");
        }

        [Fact]
        public void EmptyPlanGroup_IsNotEmitted()
        {
            Run(
                "tree = [('leaf', 0)]\n" +
                "out = _clashapply.layout(tree, plan_of(('Nothing', []), ('Real', [0])))\n" +
                "check(tree, out)\n" +
                "assert out == [('new', 'Real', [0])], out\n");
        }

        [Fact]
        public void DoublyClaimedId_LandsInExactlyOneGroup()
        {
            // clashgroup guarantees this cannot happen; the layout still must not
            // turn a malformed plan into a duplicated clash.
            Run(
                "tree = [('leaf', 0), ('leaf', 1)]\n" +
                "out = _clashapply.layout(tree, plan_of(('First', [0, 1]), ('Second', [1])))\n" +
                "check(tree, out)\n" +
                "assert out == [('new', 'First', [0, 1])], out\n");
        }

        [Fact]
        public void NestedGroupIds_RideWithTheirTopLevelGroup()
        {
            // A group node carries every id at or below it, so an untouched group
            // is copied whole and keeps its nesting.
            Run(
                "tree = [('group', 9, [0, 1, 2, 3]), ('leaf', 4)]\n" +
                "out = _clashapply.layout(tree, plan_of(('New', [4])), keep_existing=True)\n" +
                "check(tree, out)\n" +
                "assert out == [('group', 9, [0, 1, 2, 3]), ('new', 'New', [4])], out\n");
        }

        [Fact]
        public void EmptyTree_YieldsNothing()
        {
            Run("assert _clashapply.layout([], plan_of()) == []\n");
        }

        [Fact]
        public void Dissolve_TakesApartOnlyTheNamedHandles_InPlace()
        {
            Run(
                "tree = [('group', 'ours', [0, 1]), ('group', 'theirs', [2]),\n" +
                "        ('leaf', 3)]\n" +
                "out = _clashapply.dissolve(tree, set(['ours']))\n" +
                "check(tree, out)\n" +
                "assert out == [('leaf', 0), ('leaf', 1), ('group', 'theirs', [2]),\n" +
                "               ('leaf', 3)], out\n");
        }

        [Fact]
        public void Dissolve_WithNothingSelected_IsIdentity()
        {
            Run(
                "tree = [('group', 'a', [0]), ('leaf', 1)]\n" +
                "out = _clashapply.dissolve(tree, set())\n" +
                "check(tree, out)\n" +
                "assert out == tree, out\n");
        }
    }
}
