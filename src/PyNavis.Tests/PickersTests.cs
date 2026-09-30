using System;
using System.Linq;
using System.Threading;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// SelectFromList picker seam tests: STA construction (never shown), search
    /// filtering, and the checked/selected indices contract - Row.Index is the
    /// ORIGINAL item index and must survive filtering.
    /// </summary>
    public class PickersTests
    {
        private static string[] Animals => new[] { "Cat", "Dog", "Dove", "Eagle" };

        [Fact]
        public void Search_Filters_Visible_Rows()
        {
            OnSta(() =>
            {
                var window = Pickers.BuildSelectFromList("T", Animals, multiselect: true, prompt: null);
                Assert.Equal(4, Pickers.VisibleCountOf(window));
                Pickers.SetSearchForTest(window, "do");
                Assert.Equal(2, Pickers.VisibleCountOf(window));   // Dog, Dove
            });
        }

        [Fact]
        public void Checked_Indices_Are_The_Result()
        {
            OnSta(() =>
            {
                var window = Pickers.BuildSelectFromList("T", Animals, multiselect: true, prompt: null);
                Pickers.SetCheckedForTest(window, 1, true);
                Pickers.SetCheckedForTest(window, 3, true);
                Assert.Equal(new[] { 1, 3 }, Pickers.ResultOf(window).ToArray());
            });
        }

        [Fact]
        public void PreChecked_Rows_Open_Ticked_And_Stay_The_Result_Until_Changed()
        {
            // A picker for a saved choice (Hide Tabs' Shift+Click) must open with
            // that choice ticked, or every visit starts from nothing.
            OnSta(() =>
            {
                var window = Pickers.BuildSelectFromList("T", Animals, multiselect: true, prompt: null,
                                                         preChecked: new[] { 0, 2 });
                Assert.Equal(new[] { 0, 2 }, Pickers.ResultOf(window).ToArray());
                Pickers.SetCheckedForTest(window, 0, false);
                Assert.Equal(new[] { 2 }, Pickers.ResultOf(window).ToArray());

                // indices that name no row are ignored, not thrown
                var odd = Pickers.BuildSelectFromList("T", Animals, true, null, new[] { 9, -1, 3 });
                Assert.Equal(new[] { 3 }, Pickers.ResultOf(odd).ToArray());
            });
        }

        [Fact]
        public void PreChecked_Row_Is_The_Pick_In_A_SingleSelect_List()
        {
            OnSta(() =>
            {
                var window = Pickers.BuildSelectFromList("T", Animals, multiselect: false, prompt: null,
                                                         preChecked: new[] { 1 });
                Assert.Equal(new[] { 1 }, Pickers.ResultOf(window).ToArray());
            });
        }

        [Fact]
        public void Search_Does_Not_Change_Result_Indices()
        {
            OnSta(() =>
            {
                var window = Pickers.BuildSelectFromList("T", Animals, multiselect: true, prompt: null);
                Pickers.SetSearchForTest(window, "dove");
                Pickers.SetCheckedForTest(window, 0, true);        // first VISIBLE row = Dove
                Assert.Equal(new[] { 2 }, Pickers.ResultOf(window).ToArray()); // original index
            });
        }

        [Fact]
        public void SingleSelect_Keeps_Pick_When_Search_Still_Matches_It()
        {
            OnSta(() =>
            {
                var window = Pickers.BuildSelectFromList("T", Animals, multiselect: false, prompt: null);
                Pickers.SetSelectedForTest(window, 1);             // Dog
                Pickers.SetSearchForTest(window, "do");            // still matches Dog (and Dove)
                Assert.Equal(new[] { 1 }, Pickers.ResultOf(window).ToArray());

                Pickers.SetSearchForTest(window, "eagle");         // no longer matches Dog
                Assert.Null(Pickers.ResultOf(window));
            });
        }

        [Fact]
        public void CommandSwitch_Click_Is_The_Answer()
        {
            OnSta(() =>
            {
                var window = Pickers.BuildCommandSwitch("pyNavis", "Pick one", new[] { "A", "B", "C" });
                Pickers.ClickForTest(window, 2);
                Assert.Equal(2, Pickers.SwitchResultOf(window));

                // title and prompt are separate: the titlebar carries the title
                // and the label above the buttons carries the question. They used
                // to share one argument, so forms.ask_options(title=...) was
                // simply discarded.
                Assert.Equal("pyNavis", window.Title);
                var label = (System.Windows.Controls.TextBlock)
                    ((System.Windows.Controls.StackPanel)window.Content).Children[0];
                Assert.Equal("Pick one", label.Text);
            });
        }

        [Fact]
        public void CommandSwitch_WithoutPrompt_IsChromeless()
        {
            OnSta(() =>
            {
                // The quick-switch look: no titlebar, no question above
                // the buttons, just the choices. An empty prompt is the switch.
                var window = Pickers.BuildCommandSwitch("Copy State", "", new[] { "A", "B" });
                Assert.Equal(System.Windows.WindowStyle.None, window.WindowStyle);

                var body = (System.Windows.Controls.StackPanel)
                    ((System.Windows.Controls.Border)window.Content).Child;
                Assert.IsNotType<System.Windows.Controls.TextBlock>(body.Children[0]);

                // Clicking is still the answer, and Esc still means cancel.
                Pickers.ClickForTest(window, 1);
                Assert.Equal(1, Pickers.SwitchResultOf(window));
            });
        }

        [Fact]
        public void CommandSwitch_PaddingReadsTheSame_OnAllFourSides()
        {
            OnSta(() =>
            {
                // Each button carries an 8px wrap gap on its right and bottom.
                // The panel has to absorb the trailing gap of the last column
                // and the last row, or the window sits heavier at the bottom
                // and the right than at the top and the left (user-reported on
                // Copy State: 28 against 20, 32 against 24).
                var window = Pickers.BuildCommandSwitch("Copy State", "", new[] { "A", "B", "C" });
                var body = (System.Windows.Controls.StackPanel)
                    ((System.Windows.Controls.Border)window.Content).Child;
                var row = (System.Windows.Controls.WrapPanel)body.Children[0];
                var button = (System.Windows.FrameworkElement)row.Children[2];

                Assert.Equal(body.Margin.Top, body.Margin.Bottom);
                Assert.Equal(body.Margin.Left, body.Margin.Right);
                Assert.Equal(-button.Margin.Right, row.Margin.Right);
                Assert.Equal(-button.Margin.Bottom, row.Margin.Bottom);
            });
        }

        [Fact]
        public void CommandSwitch_Chromeless_SurvivesDeactivationWhileClosing()
        {
            OnSta(() =>
            {
                // Field crash: clicking an option starts the close,
                // the window deactivates mid-close, and the click-away handler
                // reentered Close() inside the host's pump, killing Navisworks.
                var window = Pickers.BuildCommandSwitch("t", "", new[] { "A", "B" });
                var onDeactivated = typeof(System.Windows.Window).GetMethod(
                    "OnDeactivated",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                window.Closing += (s, e) =>
                    onDeactivated.Invoke(window, new object[] { System.EventArgs.Empty });

                window.Show();
                Pickers.ClickForTest(window, 0);
                Assert.Equal(0, Pickers.SwitchResultOf(window));
            });
        }

        [Fact]
        public void AskNumber_Validates_Range_Live()
        {
            OnSta(() =>
            {
                var window = Pickers.BuildAskNumber("Tolerance", min: 0, max: 10, initial: 5);
                Assert.True(Pickers.OkEnabledOf(window));
                Pickers.SetNumberTextForTest(window, "50");
                Assert.False(Pickers.OkEnabledOf(window));
                Pickers.SetNumberTextForTest(window, "7.5");
                Assert.True(Pickers.OkEnabledOf(window));
                Assert.Equal(7.5, Pickers.NumberResultOf(window));
            });
        }

        /// <summary>
        /// The row template binds Label and Checked, and WPF binding resolves CLR
        /// PROPERTIES only: a public field renders as nothing at all, silently, which
        /// is what shipped every picker row blank. Binding a probe element against a
        /// real row proves the property paths resolve without needing a rendered window.
        /// </summary>
        [Fact]
        public void Row_Binds_Its_Label_And_Checked_State()
        {
            OnSta(() =>
            {
                var window = Pickers.BuildSelectFromList("T", Animals, multiselect: true, prompt: null);
                Pickers.SetCheckedForTest(window, 1, true);

                Assert.Equal("Cat", BoundValue(Pickers.RowForTest(window, 0), "Label"));
                Assert.Equal(false, BoundValue(Pickers.RowForTest(window, 0), "Checked"));
                Assert.Equal(true, BoundValue(Pickers.RowForTest(window, 1), "Checked"));
            });
        }

        /// <summary>What WPF itself would resolve for that path on that item.</summary>
        private static object BoundValue(object item, string path)
        {
            var probe = new System.Windows.Controls.ContentControl { DataContext = item };
            System.Windows.Data.BindingOperations.SetBinding(
                probe, System.Windows.Controls.ContentControl.ContentProperty,
                new System.Windows.Data.Binding(path));
            return probe.Content;
        }

        private static void OnSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA action failed: " + failure);
        }
    }
}
