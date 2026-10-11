using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The message dialogs size to their content, so a long message (a whole file from
    /// Ask AI's View files, a long traceback in forms.alert) must scroll inside the
    /// dialog instead of growing it past the screen and taking the buttons with it.
    /// </summary>
    public class MessageDialogLayoutTests
    {
        private static readonly string LongMessage = string.Join(Environment.NewLine,
            Enumerable.Range(1, 300).Select(i => $"  File \"script.py\", line {i}, in step_{i}"));

        [Fact]
        public void The_Dialog_Never_Grows_Taller_Than_The_Work_Area()
        {
            OnSta(() =>
            {
                var window = Dialogs.BuildAlert(LongMessage, "pyNavis");

                Assert.False(double.IsInfinity(window.MaxHeight), "MaxHeight must be bounded");
                Assert.True(window.MaxHeight <= SystemParameters.WorkArea.Height,
                    $"MaxHeight {window.MaxHeight} exceeds the work area {SystemParameters.WorkArea.Height}");
            });
        }

        [Fact]
        public void The_Message_Scrolls_Inside_The_Dialog()
        {
            OnSta(() =>
            {
                var window = Dialogs.BuildAlert(LongMessage, "pyNavis");
                var message = MessageBlockOf(window);

                Assert.IsType<ScrollViewer>(message.Parent);
                var scroll = (ScrollViewer)message.Parent;
                Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            });
        }

        [Theory]
        [InlineData("alert")]
        [InlineData("confirm")]
        [InlineData("ask")]
        public void A_Long_Message_Leaves_The_Buttons_Inside_The_Dialog(string kind)
        {
            OnSta(() =>
            {
                var window = kind == "alert" ? Dialogs.BuildAlert(LongMessage, "pyNavis", "copy me")
                    : kind == "confirm" ? Dialogs.BuildConfirm(LongMessage, "pyNavis")
                    : Dialogs.BuildAskString(LongMessage, "value", "pyNavis");

                // Lay the content out the way the window would at its maximum height.
                const double height = 600;
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(520, height));
                content.Arrange(new Rect(0, 0, 520, Math.Min(height, content.DesiredSize.Height)));

                foreach (var button in Dialogs.ButtonsForTest(window))
                {
                    var bottom = button.TranslatePoint(new Point(0, button.ActualHeight), content).Y;
                    Assert.True(bottom <= height, $"'{button.Content}' ends at {bottom}, below {height}");
                }
            });
        }

        [Fact]
        public void A_Short_Message_Still_Sizes_The_Dialog_To_Its_Content()
        {
            OnSta(() =>
            {
                var window = Dialogs.BuildAlert("Saved 3 viewpoints.", "pyNavis");
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(520, 600));

                Assert.True(content.DesiredSize.Height < 200,
                    $"a one-line alert wants {content.DesiredSize.Height}px");
            });
        }

        private static TextBlock MessageBlockOf(Window window)
        {
            var text = Dialogs.MessageTextOf(window);
            return Descendants((DependencyObject)window.Content)
                .OfType<TextBlock>().First(b => b.Text == text);
        }

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(
            DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                yield return child;
                foreach (var deep in Descendants(child)) yield return deep;
            }
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
