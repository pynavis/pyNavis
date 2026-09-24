using System.Windows.Input;
using PyNavis.Runtime.Execution;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Modifier-click routing: Alt opens the bundle folder
    /// and wins over Shift, Shift runs the config action, Ctrl is ignored so
    /// chords and plain clicks behave identically.
    /// </summary>
    public class ClickActionsTests
    {
        [Theory]
        [InlineData(ModifierKeys.None, ClickAction.Primary)]
        [InlineData(ModifierKeys.Control, ClickAction.Primary)]
        [InlineData(ModifierKeys.Shift, ClickAction.Config)]
        [InlineData(ModifierKeys.Control | ModifierKeys.Shift, ClickAction.Config)]
        [InlineData(ModifierKeys.Alt, ClickAction.OpenFolder)]
        [InlineData(ModifierKeys.Alt | ModifierKeys.Shift, ClickAction.OpenFolder)]
        public void Modifiers_MapToActions(ModifierKeys modifiers, ClickAction expected)
        {
            Assert.Equal(expected, ClickActions.For(modifiers));
        }

        [Fact]
        public void ExplorerArgs_SelectsTheQuotedScript()
        {
            Assert.Equal("/select,\"D:\\x\\My Tool.pushbutton\\script.py\"",
                ClickActions.ExplorerArgs(@"D:\x\My Tool.pushbutton\script.py"));
        }
    }
}
