using System.Windows.Input;
using PyNavis.Runtime.Ribbon;
using Xunit;

namespace PyNavis.Tests
{
    public class DockPaneRibbonTests
    {
        [Fact]
        public void Overflow_Message_Names_The_Panel_And_Points_At_More_Slots()
        {
            var message = DockPaneButtons.OverflowMessage("Clash navigator", pendingRestart: false);
            Assert.StartsWith("No panel slot free", message.headline);
            Assert.Contains("Clash navigator", message.headline);
            Assert.Contains("Panel slots", message.detail);
            Assert.DoesNotContain("\u2014", message.headline);   // no em dashes
        }

        [Fact]
        public void Pending_Restart_Message_Does_Not_Tell_The_User_To_Add_More_Slots()
        {
            var message = DockPaneButtons.OverflowMessage("Clash navigator", pendingRestart: true);
            Assert.DoesNotContain("No panel slot free", message.headline);
            Assert.Contains("restart", message.headline, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("restart", message.detail, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\u2014", message.headline);   // no em dashes
            Assert.DoesNotContain("\u2014", message.detail);
        }

        [Fact]
        public void Pressed_Icon_Uses_The_On_Art_When_Present()
        {
            Assert.Equal("on.png", DockPaneButtons.IconFor("off.png", "on.png", pressed: true));
            Assert.Equal("off.png", DockPaneButtons.IconFor("off.png", "on.png", pressed: false));
            Assert.Equal("off.png", DockPaneButtons.IconFor("off.png", null, pressed: true));
        }

        [Fact]
        public void Alt_Opens_The_Folder_Even_With_Other_Modifiers_Held()
        {
            Assert.True(DockPaneButtons.OpensFolder(ModifierKeys.Alt));
            Assert.True(DockPaneButtons.OpensFolder(ModifierKeys.Alt | ModifierKeys.Shift));
            Assert.True(DockPaneButtons.OpensFolder(ModifierKeys.Alt | ModifierKeys.Control));
            // Shift alone would mean config.py on a pushbutton; a dockpane has none,
            // so it falls through to the ordinary show/hide toggle.
            Assert.False(DockPaneButtons.OpensFolder(ModifierKeys.Shift));
            Assert.False(DockPaneButtons.OpensFolder(ModifierKeys.None));
        }
    }
}
