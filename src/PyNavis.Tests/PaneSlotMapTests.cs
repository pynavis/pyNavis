using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Panes;
using Xunit;

namespace PyNavis.Tests
{
    public class PaneSlotMapTests
    {
        private static Dictionary<string, int> Map(params (string key, int slot)[] pairs)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in pairs) map[pair.key] = pair.slot;
            return map;
        }

        [Fact]
        public void First_Sight_Claims_Lowest_Free_Slot_In_Order()
        {
            var result = PaneSlotMap.Resolve(new[] { "a", "b" }, Map(), 5);
            Assert.Equal(1, result.Single(r => r.BundleKey == "a").Slot);
            Assert.Equal(2, result.Single(r => r.BundleKey == "b").Slot);
        }

        [Fact]
        public void Existing_Claims_Are_Kept_Even_When_A_New_Pane_Sorts_First()
        {
            var result = PaneSlotMap.Resolve(new[] { "aardvark", "b" }, Map(("b", 1)), 5);
            Assert.Equal(1, result.Single(r => r.BundleKey == "b").Slot);
            Assert.Equal(2, result.Single(r => r.BundleKey == "aardvark").Slot);
        }

        [Fact]
        public void A_Gap_Left_By_An_Uninstalled_Pane_Is_Reused()
        {
            var result = PaneSlotMap.Resolve(new[] { "b", "new" }, Map(("a", 1), ("b", 2)), 5);
            Assert.Equal(2, result.Single(r => r.BundleKey == "b").Slot);
            Assert.Equal(1, result.Single(r => r.BundleKey == "new").Slot);
        }

        [Fact]
        public void Overflow_Panes_Get_Slot_Zero_And_Keep_Their_Order()
        {
            var keys = new[] { "a", "b", "c" };
            var result = PaneSlotMap.Resolve(keys, Map(), 2);
            Assert.Equal(0, result.Single(r => r.BundleKey == "c").Slot);
        }

        [Fact]
        public void A_Stale_Claim_Beyond_The_Slot_Count_Is_Reassigned()
        {
            var result = PaneSlotMap.Resolve(new[] { "a" }, Map(("a", 9)), 5);
            Assert.Equal(1, result.Single(r => r.BundleKey == "a").Slot);
        }

        [Fact]
        public void Config_Round_Trips_Assignments_And_Extra_Slots()
        {
            var path = Path.Combine(Path.GetTempPath(), "pynavis_tests",
                Guid.NewGuid().ToString("N"), "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{\"theme\":\"dark\"}");

            PyNavisConfig.SavePaneAssignments(path, Map(("T.tab/P.panel/X.dockpane", 3)), 15);

            var reloaded = PyNavisConfig.Load(path);
            Assert.Equal(3, reloaded.PaneAssignments["T.tab/P.panel/X.dockpane"]);
            Assert.Equal(15, reloaded.ExtraPaneSlots);
            Assert.Equal("dark", reloaded.Theme);   // other keys survive

            Directory.Delete(Path.GetDirectoryName(path), true);
        }
    }
}
