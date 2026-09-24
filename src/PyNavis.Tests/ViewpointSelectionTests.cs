using System.Collections.Generic;
using System.Linq;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Selection is held by GUID, so it survives anything that reshuffles the
    /// tree. Folder ticks cascade to descendants; ranges and invert work over
    /// the currently visible order, not the whole document.
    /// </summary>
    public class ViewpointSelectionTests
    {
        private static List<ViewpointRow> Rows() => new List<ViewpointRow>
        {
            new ViewpointRow { Guid = "g0", Key = "1", Folder = "", Name = "Level 1", Kind = "folder", IsFolder = true },
            new ViewpointRow { Guid = "g1", Key = "1/0", Folder = "Level 1", Name = "A", Kind = "viewpoint" },
            new ViewpointRow { Guid = "g2", Key = "1/1", Folder = "Level 1", Name = "B", Kind = "viewpoint" },
            // sibling whose key shares the "1" prefix: must not be caught by the cascade
            new ViewpointRow { Guid = "g3", Key = "10", Folder = "", Name = "Level 10", Kind = "folder", IsFolder = true },
            new ViewpointRow { Guid = "g4", Key = "10/0", Folder = "Level 10", Name = "C", Kind = "viewpoint" },
            new ViewpointRow { Guid = "g5", Key = "2", Folder = "", Name = "Walk", Kind = "animation" },
        };

        [Fact]
        public void FolderTick_CascadesToDescendantsOnly()
        {
            var sel = new ViewpointSelection(Rows());

            sel.Set("g0", true);

            Assert.True(sel.IsSelected("g1"));
            Assert.True(sel.IsSelected("g2"));
            Assert.False(sel.IsSelected("g4"));   // "10/0" is not under "1"
            Assert.Equal(3, sel.Count);           // the folder plus its two children
        }

        [Fact]
        public void SetRange_SelectsTheInclusiveSpanOfVisibleRows()
        {
            var sel = new ViewpointSelection(Rows());
            var visible = new List<int> { 1, 2, 4, 5 };

            sel.SetRange(visible, 1, 3, true);    // positions 1..3 => rows 2, 4, 5

            Assert.False(sel.IsSelected("g1"));
            Assert.True(sel.IsSelected("g2"));
            Assert.True(sel.IsSelected("g4"));
            Assert.True(sel.IsSelected("g5"));
        }

        [Fact]
        public void Invert_FlipsOnlyVisibleRows()
        {
            var sel = new ViewpointSelection(Rows());
            var visible = new List<int> { 1, 2 };
            sel.Set("g1", true);

            sel.Invert(visible);

            Assert.False(sel.IsSelected("g1"));
            Assert.True(sel.IsSelected("g2"));
            Assert.False(sel.IsSelected("g5"));   // not visible, untouched
        }

        [Fact]
        public void SelectedUnder_CountsDescendantsForTheRail()
        {
            var sel = new ViewpointSelection(Rows());
            sel.Set("g1", true);
            sel.Set("g4", true);

            Assert.Equal(1, sel.SelectedUnder("1"));
            Assert.Equal(1, sel.SelectedUnder("10"));
        }

        [Fact]
        public void Summary_BreaksDownByKind_AndIsEmptyAtZero()
        {
            var sel = new ViewpointSelection(Rows());
            Assert.Equal("", sel.Summary());

            sel.Set("g0", true);                  // folder + 2 viewpoints
            sel.Set("g5", true);                  // animation

            Assert.Equal("2 viewpoints, 1 animation, 1 folder", sel.Summary());
        }

        [Fact]
        public void Changed_FiresOncePerGesture_NotOncePerRow()
        {
            var sel = new ViewpointSelection(Rows());
            var fired = 0;
            sel.Changed += () => fired++;

            sel.Set("g0", true);                  // cascades over three rows
            sel.SetAll(new List<int> { 1, 2, 4 }, true);

            Assert.Equal(2, fired);
        }

        [Fact]
        public void Clear_EmptiesEverything()
        {
            var sel = new ViewpointSelection(Rows());
            sel.Set("g0", true);

            sel.Clear();

            Assert.Equal(0, sel.Count);
            Assert.Equal(0, sel.SelectedUnder("1"));
        }
    }
}
