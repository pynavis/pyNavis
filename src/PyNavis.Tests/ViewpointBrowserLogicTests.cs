using System.Collections.Generic;
using System.Linq;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The browser's pure half: duplicate marking, filtering (scope, chip,
    /// query), empty-folder detection, and the contents readout. No WPF, so
    /// the rules that decide what a user sees are testable on their own.
    /// </summary>
    public class ViewpointBrowserLogicTests
    {
        private static ViewpointRow R(string guid, string key, string parent, string folder,
            string name, string kind, int depth, int comments = 0) => new ViewpointRow
            {
                Guid = guid,
                Key = key,
                ParentKey = parent,
                Folder = folder,
                Name = name,
                Kind = kind,
                Depth = depth,
                IsFolder = kind == "folder",
                Comments = comments,
            };

        // Level 1 and Level 10 are siblings on purpose: a prefix match without
        // the separator would fold one into the other.
        private static List<ViewpointRow> Rows() => new List<ViewpointRow>
        {
            R("g0", "0", "", "", "Level 1", "folder", 0),
            R("g1", "0/0", "0", "Level 1", "North", "viewpoint", 1, 2),
            R("g2", "0/1", "0", "Level 1", "north ", "viewpoint", 1),
            R("g3", "1", "", "", "Level 10", "folder", 0),
            R("g4", "1/0", "1", "Level 10", "Roof", "viewpoint", 1),
            R("g5", "2", "", "", "Empty", "folder", 0),
            R("g6", "2/0", "2", "Empty", "Deeper", "folder", 1),
            R("g7", "3", "", "", "Walk", "animation", 0),
        };

        [Fact]
        public void MarkDuplicates_IsTrimmedAndCaseInsensitive_AndSkipsFolders()
        {
            var rows = Rows();
            ViewpointFilter.MarkDuplicates(rows);

            Assert.True(rows.Single(r => r.Guid == "g1").Duplicate);
            Assert.True(rows.Single(r => r.Guid == "g2").Duplicate);
            Assert.False(rows.Single(r => r.Guid == "g4").Duplicate);
            Assert.DoesNotContain(rows.Where(r => r.IsFolder), r => r.Duplicate);
        }

        [Fact]
        public void Scope_TakesAFolderAndItsDescendants_ButNotAPrefixSibling()
        {
            var rows = Rows();
            var visible = ViewpointFilter.Apply(rows, "", "Level 1", null)
                .Select(i => rows[i].Guid).ToList();

            Assert.Equal(new[] { "g1", "g2" }, visible);
        }

        [Fact]
        public void Query_MatchesNameAndFolder_CaseInsensitive()
        {
            var rows = Rows();
            Assert.Equal(new[] { "g1", "g2" },
                ViewpointFilter.Apply(rows, "NORTH", "", null).Select(i => rows[i].Guid));
            Assert.Equal(new[] { "g4" },
                ViewpointFilter.Apply(rows, "level 10", "", null).Select(i => rows[i].Guid));
        }

        [Fact]
        public void Chips_NarrowToAnimationsOrDuplicates()
        {
            var rows = Rows();
            ViewpointFilter.MarkDuplicates(rows);

            Assert.Equal(new[] { "g7" },
                ViewpointFilter.Apply(rows, "", "", "animations").Select(i => rows[i].Guid));
            Assert.Equal(new[] { "g1", "g2" },
                ViewpointFilter.Apply(rows, "", "", "duplicates").Select(i => rows[i].Guid));
        }

        [Fact]
        public void FoldersNeverAppearInTheList()
        {
            var rows = Rows();
            var visible = ViewpointFilter.Apply(rows, "", "", null);

            Assert.DoesNotContain(visible, i => rows[i].IsFolder);
            Assert.Equal(4, visible.Count);   // North, north, Roof, Walk
        }

        [Fact]
        public void EmptyFolderKeys_FindsNestedEmptyChains()
        {
            Assert.Equal(new[] { "2", "2/0" }, ViewpointFilter.EmptyFolderKeys(Rows()));
        }

        [Fact]
        public void StatsText_CountsEachKind()
        {
            Assert.Equal("4 folders, 3 viewpoints, 1 animation",
                ViewpointFilter.StatsText(Rows()));
        }
    }
}
