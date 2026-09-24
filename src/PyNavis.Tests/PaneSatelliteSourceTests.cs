using PyNavis.Runtime.Panes;
using Xunit;

namespace PyNavis.Tests
{
    public class PaneSatelliteSourceTests
    {
        [Fact]
        public void Emits_One_Type_Per_Slot_With_Matching_Plugin_Ids()
        {
            var source = PaneSatelliteSource.Emit(6, 8);
            Assert.Contains("public class PaneSlot6 : PyNavis.PaneSlotBase", source);
            Assert.Contains("public class PaneSlot8 : PyNavis.PaneSlotBase", source);
            Assert.DoesNotContain("PaneSlot9", source);
            Assert.Contains("[Plugin(\"PyNavis.Pane6\", \"PYNV\"", source);
            Assert.Contains("DisplayName = \"pyNavis Panel 6\"", source);
        }

        [Fact]
        public void Emitted_Source_Is_CSharp_5()
        {
            // The in-box Framework compiler is C# 5: no interpolation, no expression
            // bodies, no auto-property initializers, no nameof.
            var source = PaneSatelliteSource.Emit(6, 7);
            Assert.DoesNotContain("$\"", source);
            Assert.DoesNotContain("=>", source);
            Assert.DoesNotContain("nameof(", source);
        }

        [Fact]
        public void An_Empty_Range_Emits_No_Types()
        {
            Assert.DoesNotContain("PaneSlot", PaneSatelliteSource.Emit(6, 5));
        }
    }
}
