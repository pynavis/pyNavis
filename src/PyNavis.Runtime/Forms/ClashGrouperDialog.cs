using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using PyNavis.Runtime.Output;
using Tokens = PyNavis.Runtime.Forms.DesignSystem.Tokens;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// The Clash Grouper dialog, built to the pyNavis design system v2
    /// (docs/design-system): one solid paper surface, hairline-divided sections,
    /// native affordances (checkbox, segmented control, toggle), accent only in
    /// the primary action, the selection rail and the focus ring, numbers as
    /// muted tabular text, and the 28px "readout" as the single signature
    /// element. Code-only WPF; still a dumb view over a GrouperConfig.
    /// Build() constructs without showing - that is the unit-test seam.
    /// </summary>
    public static partial class ClashGrouperDialog
    {
        // ---- interop POCOs (constructed and read from IronPython) --------------

        public sealed class TestRow
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public int Total { get; set; } = -1;
            public int Grouped { get; set; } = -1;
            public bool Checked { get; set; }
            /// <summary>The count provider's exception message, set only when
            /// Total is CountFailedSentinel. Carried on the row so the failed
            /// count cell's tooltip can name what went wrong.</summary>
            public string CountError { get; set; } = "";
        }

        public sealed class RuleChoice
        {
            public string Id { get; set; }
            public string Label { get; set; }
        }

        public sealed class GrouperConfig
        {
            public List<int> TestIndexes { get; set; } = new List<int>();
            public bool Smart { get; set; } = true;
            public List<string> RuleIds { get; set; } = new List<string>();
            public double ToleranceMeters { get; set; } = 2.0;
            public bool KeepExisting { get; set; } = true;
            /// <summary>"apply", "ungroup", or "" when cancelled.</summary>
            public string Action { get; set; } = "";
        }

        public sealed class PreviewGroup
        {
            public string Name { get; set; }
            public int Count { get; set; }
        }

        public sealed class PreviewData
        {
            public int TotalClashes { get; set; }
            public int TotalGroups { get; set; }
            public string Note { get; set; } = "";
            public List<PreviewGroup> Groups { get; set; } = new List<PreviewGroup>();
        }

        /// <summary>Progress channel handed to the preview provider. Returns
        /// false once the user has hit Cancel, which is the provider's cue to
        /// stop and return null.</summary>
        public delegate bool PreviewProgress(double fraction, string stage);

        public sealed class TestCounts
        {
            public int Total { get; set; }
            public int Grouped { get; set; }
        }

        // ---- script-facing API -------------------------------------------------

        /// <summary>Shows the dialog; returns the config with Action set, or null.
        /// Optional defaults (from the bundle's saved settings) preset mode,
        /// tolerance, keep-existing, and the rule chain.</summary>
        public static GrouperConfig Show(
            IList<TestRow> tests, IList<RuleChoice> rules,
            Func<GrouperConfig, PreviewProgress, PreviewData> preview,
            Func<int, TestCounts> counts = null, GrouperConfig defaults = null)
        {
            var window = Build(tests, rules, preview, counts, defaults);
            window.ShowDialog();
            var config = PartsOf(window).Result;
            return config != null && config.Action.Length > 0 ? config : null;
        }

        // Palette (Tokens) lives in the shared DesignSystem, aliased above, so the
        // grouper and every other pyNavis form draw from one source of truth.

        // ---- internal state ----------------------------------------------------

        private sealed class TestRowUi
        {
            public int Index;
            /// <summary>Position in parts.TestRows / parts.Tests. Filtering and
            /// sorting reorder only the visual children, so this stays put and
            /// is what every positional reader means.</summary>
            public int Pos;
            /// <summary>The model row this UI shows. Held directly so the filter
            /// and the count sort never have to guess at an index.</summary>
            public TestRow Row;
            public bool Checked;
            public Button Shell;
            public Border Rail;
            public Border CheckBox;
            public TextBlock CheckGlyph;
            public TextBlock CountText;
            public TextBlock GroupedText;
        }

        private sealed class Disclosure
        {
            public bool Open;
            public TextBlock Summary;
            public Path Chevron;
            public FrameworkElement Content;
        }

        private enum PreviewState
        {
            NoTests, NeedsSelection, NeedsRule, Ready, Working, Done, Stale, Failed
        }

        // Drawn chevrons: font glyphs carry baseline offsets that render
        // off-center inside a small circle; geometry centers exactly.
        private static readonly Geometry ChevronDown = Geometry.Parse("M 0,0 L 4,4 L 8,0");
        private static readonly Geometry ChevronUp = Geometry.Parse("M 0,4 L 4,0 L 8,4");

        private sealed class Parts
        {
            public IList<TestRow> Tests;
            public IList<RuleChoice> Rules;
            public Func<GrouperConfig, PreviewProgress, PreviewData> Preview;
            public Func<int, TestCounts> Counts;
            public Tokens T;
            public Color Accent;
            public Style FocusRing;

            public List<TestRowUi> TestRows = new List<TestRowUi>();
            /// <summary>The rows the list is showing right now, in the order it
            /// is showing them. A view over TestRows, never a replacement for
            /// it: All/None act on this, everything positional acts on TestRows.
            /// </summary>
            public List<TestRowUi> VisibleRows = new List<TestRowUi>();
            public StackPanel TestList;
            public ScrollViewer TestScroll;
            public TextBox Filter;
            public TextBlock FilterHint;
            public TextBlock FilterNote;
            public TextBlock NoMatchNote;
            public Button SelectAll;
            public Button SelectNone;
            public Button SortToggle;
            public bool SortByCount;
            /// <summary>How many times the list has re-parented its rows. Only a
            /// changed visible sequence may bump this: re-rendering resets the
            /// scroll position, so doing it needlessly is a defect.</summary>
            public int ListRenders;
            public Disclosure TestsDisc = new Disclosure();
            public Disclosure ModeDisc = new Disclosure();
            public bool Smart = true;
            public GrouperConfig Defaults;
            public Button SegSmart;
            public Button SegCustom;
            public TextBlock ModeCaption;
            public FrameworkElement ModeSection;
            public FrameworkElement RulesArea;
            public StackPanel RuleRows;
            public StackPanel TolerancePanel;
            public Border ToleranceShell;
            public TextBox Tolerance;
            public TextBlock ToleranceWarn;
            public TextBlock EmptyChainNote;
            public bool KeepExisting = true;
            public Border ToggleShell;
            public Border ToggleThumb;
            public TranslateTransform ToggleSlide;

            public FrameworkElement Readout;
            public TextBlock StatFrom;
            public TextBlock StatTo;
            public TextBlock Subline;
            public TextBlock Message;
            public TextBlock NoteText;
            public FrameworkElement PreviewShell;
            public StackPanel PreviewList;
            public Button Apply;
            public Button Ungroup;
            public Button CancelDialog;
            public GrouperConfig Result;
            public bool Loaded;

            // Preview state machine. The dialog computes nothing until asked.
            public PreviewState State = PreviewState.Ready;
            public PreviewData Data;
            public string Signature = "";     // config the shown Data belongs to
            public string StaleNote = "";     // "Settings changed...", "Preview cancelled."
            // The state StaleNote was written for. A note only explains the state
            // that produced it, so it must not be painted in any other one.
            public PreviewState NoteFor = PreviewState.Ready;
            public bool Busy;                 // a preview is running right now
            public bool CancelRequested;
            public int PreviewTotal;          // denominator for the progress counter
            public double BarFraction;
            public int LastPump;

            public FrameworkElement TestsSection;
            public StackPanel ActionPanel;
            public TextBlock SelectionSummary;
            public TextBlock StaleText;
            public Button PreviewButton;
            public StackPanel ProgressPanel;
            public TextBlock StageText;
            public TextBlock ProgressCount;
            public Border BarTrack;
            public Border BarFill;
            public Button CancelPreview;

            // Lazy counting pass.
            public int CountIndex;
            public bool CountPaused;
            public bool SelectionTouched;
        }

        private static Parts PartsOf(Window window) => (Parts)window.Tag;

        // ---- construction ------------------------------------------------------

        public static Window Build(
            IList<TestRow> tests, IList<RuleChoice> rules,
            Func<GrouperConfig, PreviewProgress, PreviewData> preview,
            Func<int, TestCounts> counts = null, GrouperConfig defaults = null)
        {
            var parts = new Parts
            {
                Tests = tests,
                Rules = rules,
                Preview = preview,
                Counts = counts,
                T = Tokens.For(PyNavisTheme.IsDark),
                Accent = FluentChrome.AccentColor(),
                Defaults = defaults,
            };
            if (defaults != null)
            {
                parts.Smart = defaults.Smart;
                parts.KeepExisting = defaults.KeepExisting;
            }
            parts.FocusRing = BuildFocusRing(parts);

            var window = new Window
            {
                Title = "Clash Grouper",
                Width = 600,
                // Tall enough for ~11 test rows at the default size: on a
                // 54-test model the old 720 showed six and left the preview
                // panel holding a large empty area.
                Height = 880,
                MinWidth = 540,
                MinHeight = 580,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Background = Brush(parts.T.Paper),
                Tag = parts,
                ShowInTaskbar = false,
            };
            FluentChrome.Apply(window); // system corners + dark titlebar attrs; surfaces stay solid

            // SetInteractive can only disable WPF elements, and this window wears
            // the real Windows titlebar. Report pumps Win32 messages, so the X
            // button or Alt+F4 can run Close() with the preview provider still on
            // the stack: the window would vanish while the modal frame underneath
            // could not unwind until the provider returned, leaving Navisworks
            // wedged with no UI and no way to cancel. Turn both native routes
            // into the cancel the user actually meant.
            window.Closing += (s, e) =>
            {
                var live = PartsOf(window);
                if (!live.Busy) return;
                live.CancelRequested = true;
                e.Cancel = true;
            };

            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Progressive disclosure: tests and grouping collapse behind clearly
            // marked expander headers (circled chevron, live summary); expanded
            // content sits in a bold group frame. The preview - the payoff - is
            // always visible in its own titled frame and owns the height.
            parts.TestsSection = Expander(window, parts, "Clash tests",
                BuildTestsSection(window, parts), parts.TestsDisc);
            body.Children.Add(AtRow(0, parts.TestsSection));
            var mode = Expander(window, parts, "How to group",
                BuildModeSection(window, parts), parts.ModeDisc);
            parts.ModeSection = mode;
            body.Children.Add(AtRow(2, mode));
            var previewFrame = GroupFrame(parts, "Preview", BuildPreviewSection(window, parts));
            previewFrame.Margin = new Thickness(20, 8, 20, 14);
            body.Children.Add(AtRow(4, previewFrame));

            var footerShell = new Grid();
            footerShell.Children.Add(new Border
            {
                Padding = new Thickness(20, 12, 20, 12),
                Child = BuildFooter(window, parts),
            });
            footerShell.Children.Add(ResizeGripDots(parts));
            var footer = new Border
            {
                Background = Brush(parts.T.Surface),
                BorderBrush = Brush(parts.T.LineStrong),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = footerShell,
            };

            var layout = new DockPanel();
            DockPanel.SetDock(footer, Dock.Bottom);
            layout.Children.Add(footer);
            layout.Children.Add(body);
            window.Content = layout;   // real Windows titlebar, like every other window

            HookTestListHeight(window, parts);
            parts.Loaded = true;
            Refresh(window);

            // Counting walks every result, so it happens after first paint, not
            // before it. Build stays instant no matter how big the model is.
            window.ContentRendered += (s, e) => BeginCounting(window);

            return window;
        }

        // ---- shared visual helpers ---------------------------------------------

        // These forward to the shared toolkit so the grouper cannot drift from
        // every other window. They stay as thin local names only because this
        // file calls them several hundred times.
        private static SolidColorBrush Brush(Color color) => DesignSystem.Brush(color);

        private static FrameworkElement AtRow(int row, FrameworkElement element)
        {
            Grid.SetRow(element, row);
            return element;
        }

        private static TextBlock Glyph(Parts parts, char glyph, double size, Color color) =>
            DesignSystem.Glyph(glyph, size, color);

        private static TextBlock Text(Parts parts, string text, double size, Color color,
            FontWeight? weight = null) => DesignSystem.Text(text, size, color, weight);

        private static TextBlock TabularNumber(Parts parts, string text, double size, Color color) =>
            DesignSystem.TabularNumber(text, size, color);

        private static Style BuildFocusRing(Parts parts) => DesignSystem.FocusRing(parts.Accent);

        /// <summary>The shared chrome, stretched: this file builds full-width rows
        /// and dropdown faces whose labels must start at the left edge.</summary>
        private static ControlTemplate ButtonChrome(CornerRadius radius) =>
            DesignSystem.ButtonChrome(radius, stretchContent: true);

        /// <summary>A chromeless button carrying arbitrary content: the base for
        /// rows, segments, dropdown faces and icon buttons in this dialog.</summary>
        private static Button BareButton(Parts parts, FrameworkElement content, double radius = 4)
        {
            return new Button
            {
                Content = content,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Template = ButtonChrome(new CornerRadius(radius)),
                FocusVisualStyle = parts.FocusRing,
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
        }

        /// <summary>Paints a hover background, restoring whatever the rest state
        /// is at that moment (rest is a factory because it can change: a ticked
        /// row keeps its selection wash).</summary>
        private static void Hover(Button button, Func<Color?> rest, Color hover)
        {
            button.MouseEnter += (s, e) =>
            {
                if (button.IsEnabled) button.Background = Brush(hover);
            };
            button.MouseLeave += (s, e) =>
            {
                if (!button.IsEnabled) return;
                var color = rest();
                button.Background = color.HasValue ? Brush(color.Value) : Brushes.Transparent;
            };
        }

        /// <summary>
        /// A collapsible section: circled chevron on the LEFT, title, live
        /// summary on the right, and a visible separator, per the disclosure
        /// card. Collapsed by default when the defaults are right; Refresh
        /// auto-expands a section that needs attention and never auto-closes.
        /// </summary>
        private static FrameworkElement Expander(
            Window window, Parts parts, string title, FrameworkElement content, Disclosure disc)
        {
            disc.Content = content;

            disc.Chevron = new Path
            {
                Data = ChevronDown,
                Stroke = Brush(parts.T.Ink),
                StrokeThickness = 1.4,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                Width = 8,
                Height = 5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var circle = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                BorderBrush = Brush(parts.T.Ink),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Child = disc.Chevron,
            };

            var label = Text(parts, title, 14, parts.T.Ink, FontWeights.SemiBold);
            label.VerticalAlignment = VerticalAlignment.Center;

            disc.Summary = TabularNumber(parts, "", 12, parts.T.Muted);
            disc.Summary.VerticalAlignment = VerticalAlignment.Center;
            disc.Summary.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(disc.Summary, Dock.Right);

            var header = new DockPanel { Height = 40, Margin = new Thickness(20, 0, 20, 0) };
            header.Children.Add(circle);
            header.Children.Add(disc.Summary);
            header.Children.Add(label);

            var headerButton = BareButton(parts, header, radius: 0);
            headerButton.Click += (s, e) =>
            {
                disc.Open = !disc.Open;
                Refresh(window);
            };
            Hover(headerButton, () => null, parts.T.Surface);

            var frame = GroupFrame(parts, title, content);
            frame.Margin = new Thickness(20, 0, 20, 10);
            disc.Content = frame;

            var section = new StackPanel();
            section.Children.Add(headerButton);
            section.Children.Add(new Border
            {
                BorderBrush = Brush(parts.T.LineStrong),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(20, 0, 20, 10),
            });
            section.Children.Add(frame);
            return section;
        }

        /// <summary>Shows or hides a section and turns its chevron.</summary>
        private static void PaintDisclosure(Disclosure disc)
        {
            if (disc?.Content == null) return;
            disc.Content.Visibility = disc.Open ? Visibility.Visible : Visibility.Collapsed;
            if (disc.Chevron != null) disc.Chevron.Data = disc.Open ? ChevronUp : ChevronDown;
        }

        private static Grid GroupFrame(Parts parts, string title, FrameworkElement content)
        {
            var frame = new Border
            {
                BorderBrush = Brush(parts.T.FrameLine),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(14, title != null ? 16 : 14, 14, 14),
                Margin = new Thickness(0, title != null ? 8 : 0, 0, 0),
                Child = content,
            };
            var grid = new Grid();
            grid.Children.Add(frame);
            if (title != null)
            {
                var label = Text(parts, title, 12, parts.T.Ink, FontWeights.Medium);
                label.Background = Brush(parts.T.Paper);
                label.Padding = new Thickness(5, 0, 5, 0);
                label.Margin = new Thickness(12, 0, 0, 0);
                label.HorizontalAlignment = HorizontalAlignment.Left;
                label.VerticalAlignment = VerticalAlignment.Top;
                grid.Children.Add(label);
            }
            return grid;
        }

        /// <summary>Quiet scrollbar: thin track-less thumb that never shouts.
        /// One inline-parsed template (Track's parts are not settable through
        /// FrameworkElementFactory) - still code-only, no .xaml files.</summary>
        private static void SlimScroll(ScrollViewer scroll)
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='ScrollBar'>" +
                "<Setter Property='Width' Value='10'/>" +
                "<Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='ScrollBar'>" +
                "<Grid Background='Transparent'>" +
                "<Track x:Name='PART_Track' IsDirectionReversed='True'>" +
                "<Track.DecreaseRepeatButton>" +
                "<RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>" +
                "</Track.DecreaseRepeatButton>" +
                "<Track.IncreaseRepeatButton>" +
                "<RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/>" +
                "</Track.IncreaseRepeatButton>" +
                "<Track.Thumb><Thumb Focusable='False'><Thumb.Template>" +
                "<ControlTemplate TargetType='Thumb'>" +
                "<Border Background='#59808080' CornerRadius='3' Margin='3,1'/>" +
                "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
                "</Track></Grid></ControlTemplate></Setter.Value></Setter></Style>";
            scroll.Resources.Add(typeof(ScrollBar),
                (Style)System.Windows.Markup.XamlReader.Parse(xaml));
        }

        private static Border ListContainer(Parts parts, UIElement content) => new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderBrush = Brush(parts.T.LineStrong),
            BorderThickness = new Thickness(1),
            Background = Brush(parts.T.Paper),
            Child = content,
        };

        /// <summary>Classic diagonal-dot resize grip: the window IS resizable
        /// (chrome resize borders), and the corner should say so.</summary>
        private static FrameworkElement ResizeGripDots(Parts parts)
        {
            var canvas = new Canvas
            {
                Width = 11,
                Height = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 3, 3),
                IsHitTestVisible = false,
            };
            foreach (var (x, y) in new[] { (8, 8), (8, 4), (4, 8), (8, 0), (4, 4), (0, 8) })
            {
                var dot = new Ellipse { Width = 2.5, Height = 2.5, Fill = Brush(parts.T.Muted) };
                Canvas.SetLeft(dot, x);
                Canvas.SetTop(dot, y);
                canvas.Children.Add(dot);
            }
            return canvas;
        }

        // ---- tests section -----------------------------------------------------

        /// <summary>Test row height, and the space the rest of the dialog needs
        /// around the list. The list's MaxHeight is the window's height minus
        /// that reserve, so dragging the window taller buys test rows instead of
        /// more empty preview panel. The floor keeps six rows visible even at
        /// MinHeight, which is what the list used to show at any size.</summary>
        private const double TestRowHeight = 32;
        private const double TestListReserve = 520;
        private const double TestListFloor = 6 * TestRowHeight + 8;

        private static double TestListMaxHeight(double windowHeight) =>
            Math.Max(TestListFloor, windowHeight - TestListReserve);

        private static void HookTestListHeight(Window window, Parts parts)
        {
            if (parts.TestScroll == null) return;   // no tests, so no list
            // Height, not ActualHeight: Build never shows the window, so the
            // first real measure has not happened yet.
            parts.TestScroll.MaxHeight = TestListMaxHeight(window.Height);
            window.SizeChanged += (s, e) =>
                parts.TestScroll.MaxHeight = TestListMaxHeight(window.ActualHeight);
        }

        private static FrameworkElement BuildTestsSection(Window window, Parts parts)
        {
            if (parts.Tests.Count == 0)
                return Text(parts, "No clash tests found. Run Clash Detective first.", 13, parts.T.Muted);

            parts.TestList = new StackPanel();
            for (var pos = 0; pos < parts.Tests.Count; pos++)
            {
                var test = parts.Tests[pos];
                var ui = new TestRowUi
                {
                    Index = test.Index,
                    Pos = pos,
                    Row = test,
                    Checked = test.Checked,
                };

                ui.Rail = new Border { Width = 3, Background = Brushes.Transparent };
                ui.CheckGlyph = Glyph(parts, (char)0xE73E, 9, Colors.White);
                ui.CheckBox = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(9, 0, 10, 0),
                    Child = ui.CheckGlyph,
                };

                var inner = new DockPanel { LastChildFill = true };
                inner.Children.Add(ui.Rail);
                inner.Children.Add(ui.CheckBox);

                // Docked Right, so the FIRST one added is the rightmost: the
                // grouped count goes in first and the total reads to its left.
                // The total is the primary number and must come first.
                ui.GroupedText = TabularNumber(parts, "", 12, parts.T.Muted);
                ui.GroupedText.VerticalAlignment = VerticalAlignment.Center;
                ui.GroupedText.Margin = new Thickness(0, 0, 12, 0);
                ui.GroupedText.Visibility = Visibility.Collapsed;
                DockPanel.SetDock(ui.GroupedText, Dock.Right);
                inner.Children.Add(ui.GroupedText);

                ui.CountText = TabularNumber(parts, "…", 13, parts.T.Muted);
                ui.CountText.VerticalAlignment = VerticalAlignment.Center;
                // The right margin is the row's edge padding when the grouped
                // count is collapsed, and the gap between the two when it is not.
                ui.CountText.Margin = new Thickness(12, 0, 12, 0);
                DockPanel.SetDock(ui.CountText, Dock.Right);
                inner.Children.Add(ui.CountText);

                var name = Text(parts, test.Name, 13, parts.T.Ink);
                name.VerticalAlignment = VerticalAlignment.Center;
                name.TextWrapping = TextWrapping.NoWrap;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                inner.Children.Add(name);

                ui.Shell = BareButton(parts, inner, radius: 0);
                ui.Shell.Height = TestRowHeight;
                ui.Shell.BorderBrush = Brush(parts.T.Line);
                ui.Shell.BorderThickness = new Thickness(0, 0, 0, 1);
                ui.Shell.Click += (s, e) =>
                {
                    ui.Checked = !ui.Checked;
                    parts.SelectionTouched = true;
                    Refresh(window);
                };
                ui.Shell.MouseEnter += (s, e) =>
                {
                    if (!ui.Checked) ui.Shell.Background = Brush(parts.T.Surface);
                };
                ui.Shell.MouseLeave += (s, e) => PaintTestRow(parts, ui);

                parts.TestRows.Add(ui);
            }
            // RefreshTestList parents the rows: which ones are on screen, in what
            // order, and which one owns the container's bottom edge all depend on
            // the filter and the sort, so there is one place that decides it.

            parts.NoMatchNote = Text(parts, "No test matches this filter.", 13, parts.T.Muted);
            parts.NoMatchNote.Margin = new Thickness(12, 9, 12, 9);

            parts.TestScroll = new ScrollViewer
            {
                Content = parts.TestList,
                MaxHeight = TestListFloor,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            SlimScroll(parts.TestScroll);

            parts.FilterNote = Text(parts, "", 12, parts.T.Muted);
            parts.FilterNote.Margin = new Thickness(0, 8, 0, 0);
            parts.FilterNote.TextWrapping = TextWrapping.Wrap;
            parts.FilterNote.Visibility = Visibility.Collapsed;

            var section = new StackPanel();
            section.Children.Add(BuildTestsToolbar(window, parts));
            section.Children.Add(ListContainer(parts, parts.TestScroll));
            section.Children.Add(parts.FilterNote);
            return section;
        }

        /// <summary>Filter, All, None and sort as one bar directly above the
        /// list, which is where the design system's data list puts search: the
        /// filter takes the width it needs and the three buttons ride the right
        /// edge, so the whole thing costs one 32px row rather than four
        /// scattered widgets' worth of height.</summary>
        private static FrameworkElement BuildTestsToolbar(Window window, Parts parts)
        {
            parts.Filter = new TextBox();
            parts.Filter.TextChanged += (s, e) => Refresh(window);
            // Escape empties the box. On 54 tests the fast path is "open, type,
            // pick", and backing out of a filter should not mean selecting the
            // text to delete it. Only handled when there IS text, so an empty
            // box leaves Escape to whatever the dialog does with it.
            parts.Filter.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Escape) return;
                if (parts.Filter.Text.Length == 0) return;
                parts.Filter.Text = "";
                e.Handled = true;
            };
            // ...and the caret is already there when the dialog appears, so the
            // fast path starts with a keystroke rather than a click. Loaded, not
            // here: Build never shows the window, so there is no focus scope yet.
            window.Loaded += (s, e) => parts.Filter.Focus();
            var shell = DesignSystem.InputField(parts.T, parts.Filter);
            shell.Height = 32;

            // Hint rather than a label: the bar has no room for both, and an
            // empty box with "Filter by name" in it explains itself.
            parts.FilterHint = Text(parts, "Filter by name", 13, parts.T.Muted);
            parts.FilterHint.VerticalAlignment = VerticalAlignment.Center;
            parts.FilterHint.Margin = new Thickness(9, 0, 0, 0);
            parts.FilterHint.IsHitTestVisible = false;
            var field = new Grid();
            field.Children.Add(shell);
            field.Children.Add(parts.FilterHint);

            parts.SelectAll = ToolButton(parts, "All", "Tick every test the filter is showing");
            parts.SelectAll.Click += (s, e) => SetVisibleChecked(window, parts, true);
            parts.SelectNone = ToolButton(parts, "None", "Untick every test the filter is showing");
            parts.SelectNone.Margin = new Thickness(6, 0, 0, 0);
            parts.SelectNone.Click += (s, e) => SetVisibleChecked(window, parts, false);
            parts.SortToggle = ToolButton(parts, "Sort: name", null);
            parts.SortToggle.Margin = new Thickness(10, 0, 0, 0);
            parts.SortToggle.Click += (s, e) => ToggleSort(window, parts);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 0, 0, 0),
            };
            buttons.Children.Add(parts.SelectAll);
            buttons.Children.Add(parts.SelectNone);
            buttons.Children.Add(parts.SortToggle);

            var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(buttons, Dock.Right);
            bar.Children.Add(buttons);
            bar.Children.Add(field);
            return bar;
        }

        /// <summary>Small bordered neutral action, the same weight as the mode
        /// section's "Add rule" so the toolbar reads as part of this dialog.
        /// </summary>
        private static Button ToolButton(Parts parts, string label, string tip)
        {
            var text = Text(parts, label, 13, parts.T.Ink);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            var button = BareButton(parts, text);
            button.Height = 32;
            button.MinWidth = 48;
            button.Padding = new Thickness(10, 0, 10, 0);
            button.Background = Brush(parts.T.Paper);
            button.BorderBrush = Brush(parts.T.LineStrong);
            button.BorderThickness = new Thickness(1);
            button.ToolTip = tip;
            Hover(button, () => parts.T.Paper, parts.T.Surface);
            return button;
        }

        /// <summary>All and None act on what the user can see. Filtering to a
        /// prefix and clicking All is the fast path on a 54-test model, so the
        /// buttons must never reach past the filter.</summary>
        private static void SetVisibleChecked(Window window, Parts parts, bool value)
        {
            foreach (var ui in parts.VisibleRows) ui.Checked = value;
            // As deliberate as a row click, so the counting pass must not come
            // along afterwards and auto-tick over the top of it.
            parts.SelectionTouched = true;
            Refresh(window);
        }

        private static void ToggleSort(Window window, Parts parts)
        {
            parts.SortByCount = !parts.SortByCount;
            Refresh(window);
        }

        /// <summary>Decides which rows are on screen and in what order.
        ///
        /// It re-parents the existing row UIs and never rebuilds one: a rebuilt
        /// row would drop its click handler, lose Checked, and orphan the
        /// CountText/GroupedText that the counting pass writes into.
        /// parts.TestRows itself is never reordered, because PaintTestCounts,
        /// CountOne, FillCountsForTest and SetTestCheckedForTest all index it
        /// positionally against parts.Tests.</summary>
        private static void RefreshTestList(Parts parts)
        {
            if (parts.TestList == null) return;   // no tests, so no list

            var query = (parts.Filter.Text ?? "").Trim();
            // The hint tracks the QUERY, not the raw text: a box holding only
            // spaces filters nothing, so it must still read as empty.
            parts.FilterHint.Visibility =
                query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            var visible = new List<TestRowUi>();
            var hiddenTicked = 0;
            foreach (var ui in parts.TestRows)
            {
                if (query.Length == 0
                    || ui.Row.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    visible.Add(ui);
                else if (ui.Checked) hiddenTicked++;   // hiding is not deselecting
            }
            if (parts.SortByCount) SortByCountDescending(visible);

            // Only re-parent when the visible sequence really changed. Refresh
            // runs once per arriving count, 54 times on the field report's
            // model, and each re-parent is 54 Buttons out of a panel and back
            // in. That is the whole reason for this guard: work avoided, not a
            // scroll bug dodged. Clear() and the re-adds run in one synchronous
            // stack frame with no layout pass between them, so the ScrollViewer
            // never measures against an empty panel and its offset clamp never
            // gets the chance to fire.
            var changed = !SameRows(parts.VisibleRows, visible);
            parts.VisibleRows = visible;
            if (changed)
            {
                // Pin the scroll position across the re-parent. Measured: this is
                // currently a no-op, because nothing measures between the Clear
                // and the re-adds, so the offset never gets clamped and WPF hands
                // it back unchanged. Kept as two lines of defence for an
                // invariant we do care about - re-parenting rows must not move the
                // user's place in a 54-row list - so that it stays true if the
                // ordering here ever changes. Do not read it as a bug fix.
                var offset = parts.TestScroll.VerticalOffset;
                parts.TestList.Children.Clear();
                foreach (var ui in visible)
                {
                    // Restore the hairline first: after a filter or a sort the
                    // row that used to be last usually is not any more.
                    ui.Shell.BorderThickness = new Thickness(0, 0, 0, 1);
                    parts.TestList.Children.Add(ui.Shell);
                }
                if (visible.Count > 0)
                    // The visually last row lets the container's own edge be the
                    // last line.
                    visible[visible.Count - 1].Shell.BorderThickness = new Thickness(0);
                else
                    parts.TestList.Children.Add(parts.NoMatchNote);
                // ScrollViewer clamps this to the new extent on the next measure,
                // so a filter that shortens the list still lands somewhere real.
                parts.TestScroll.ScrollToVerticalOffset(offset);
                parts.ListRenders++;
            }

            parts.FilterNote.Visibility =
                query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            var note = string.Format(CultureInfo.InvariantCulture,
                "Showing {0} of {1}.", visible.Count, parts.TestRows.Count);
            if (hiddenTicked > 0)
                // The preview runs on every ticked test, not just the visible
                // ones, so a filter that hides a tick has to own up to it.
                note += string.Format(CultureInfo.InvariantCulture,
                    " {0} selected {1} hidden and would still be previewed.",
                    hiddenTicked, hiddenTicked == 1 ? "is" : "are");
            parts.FilterNote.Text = note;

            ((TextBlock)parts.SortToggle.Content).Text =
                parts.SortByCount ? "Sort: count" : "Sort: name";
            parts.SortToggle.ToolTip = parts.SortByCount
                ? "Switch back to the order the tests are listed in"
                : "Switch to clash count order, largest first";
        }

        /// <summary>Same rows in the same order? Reference equality, because
        /// these are the one-and-only row UIs, never copies.</summary>
        private static bool SameRows(List<TestRowUi> a, List<TestRowUi> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
                if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        /// <summary>Largest clash count first, and stable: equal counts keep the
        /// order walk_tests yielded, because the comparison falls back to the
        /// row's own position.
        ///
        /// Total is -1 until the lazy count lands and -2 when the count failed.
        /// Both are negative, so an unknown number has to be forced to the end
        /// rather than sorted below a genuine zero.</summary>
        private static void SortByCountDescending(List<TestRowUi> rows)
        {
            rows.Sort((a, b) =>
            {
                var byCount = CountSortKey(b).CompareTo(CountSortKey(a));
                return byCount != 0 ? byCount : a.Pos.CompareTo(b.Pos);
            });
        }

        private static long CountSortKey(TestRowUi ui) =>
            ui.Row.Total < 0 ? long.MinValue : ui.Row.Total;

        private static void PaintTestRow(Parts parts, TestRowUi ui)
        {
            ui.Shell.Background = ui.Checked ? Brush(parts.T.Selection) : Brushes.Transparent;
            ui.Rail.Background = ui.Checked ? Brush(parts.Accent) : Brushes.Transparent;
            if (ui.Checked)
            {
                ui.CheckBox.Background = Brush(parts.Accent);
                ui.CheckBox.BorderBrush = Brush(parts.Accent);
                ui.CheckGlyph.Visibility = Visibility.Visible;
            }
            else
            {
                ui.CheckBox.Background = Brushes.Transparent;
                ui.CheckBox.BorderBrush = Brush(parts.T.LineStrong);
                ui.CheckGlyph.Visibility = Visibility.Collapsed;
            }
        }

        // ---- mode section ------------------------------------------------------

        private static FrameworkElement BuildModeSection(Window window, Parts parts)
        {
            var panel = new StackPanel();

            parts.SegSmart = Segment(parts, "Smart", new CornerRadius(3, 0, 0, 3));
            parts.SegSmart.Click += (s, e) => { parts.Smart = true; Refresh(window); };
            parts.SegCustom = Segment(parts, "Custom rules", new CornerRadius(0, 3, 3, 0));
            parts.SegCustom.Click += (s, e) => { parts.Smart = false; Refresh(window); };

            var segRow = new StackPanel { Orientation = Orientation.Horizontal };
            segRow.Children.Add(parts.SegSmart);
            segRow.Children.Add(parts.SegCustom);
            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderBrush = Brush(parts.T.LineStrong),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = segRow,
            });

            parts.ModeCaption = Text(parts, "", 12, parts.T.Muted);
            parts.ModeCaption.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(parts.ModeCaption);

            var rulesArea = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            parts.RuleRows = new StackPanel();
            rulesArea.Children.Add(parts.RuleRows);
            parts.EmptyChainNote = Text(parts, "Add at least one rule to group by.", 12, parts.T.Muted);
            parts.EmptyChainNote.Margin = new Thickness(0, 0, 0, 8);
            rulesArea.Children.Add(parts.EmptyChainNote);

            // Same bordered-neutral weight as the tests toolbar, from the same
            // helper: this button was the recipe ToolButton was modelled on.
            var addRule = ToolButton(parts, "Add rule", null);
            addRule.Padding = new Thickness(14, 0, 14, 0);
            addRule.HorizontalAlignment = HorizontalAlignment.Left;
            addRule.Margin = new Thickness(24, 0, 0, 0);
            addRule.Click += (s, e) => { AddRuleRow(window, parts); Refresh(window); };
            rulesArea.Children.Add(addRule);
            parts.RulesArea = rulesArea;
            panel.Children.Add(rulesArea);

            BuildTolerancePanel(window, parts);
            // Saved rule chain (bundle settings) or one ready-to-go row for Custom.
            if (parts.Defaults != null && parts.Defaults.RuleIds.Count > 0)
                foreach (var ruleId in parts.Defaults.RuleIds)
                    AddRuleRow(window, parts, ruleId);
            else
                AddRuleRow(window, parts);

            panel.Children.Add(BuildKeepExistingRow(window, parts));
            return panel;
        }

        private static Button Segment(Parts parts, string label, CornerRadius radius)
        {
            var text = Text(parts, label, 13, parts.T.Ink);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            var segment = BareButton(parts, text);
            segment.Template = ButtonChrome(radius);
            segment.Height = 30;
            segment.Padding = new Thickness(16, 0, 16, 0);
            return segment;
        }

        private static void PaintSegments(Parts parts)
        {
            void Paint(Button segment, bool selected)
            {
                segment.Background = selected ? Brush(parts.Accent) : Brushes.Transparent;
                ((TextBlock)segment.Content).Foreground =
                    selected ? Brushes.White : Brush(parts.T.Ink);
            }
            Paint(parts.SegSmart, parts.Smart);
            Paint(parts.SegCustom, !parts.Smart);
            parts.ModeCaption.Text = parts.Smart
                ? "Smart finds the elements causing the clashes and groups them for you."
                : "Chain rules; each one splits the groups made by the one before.";
        }

        // ---- rule chain --------------------------------------------------------

        private sealed class Dropdown
        {
            public Button Shell;
            public string SelectedId;
            private readonly Parts _parts;
            private readonly Window _window;
            private readonly TextBlock _label;
            private readonly Popup _popup;

            public Dropdown(Window window, Parts parts, int defaultIndex, string selectedId = null)
            {
                _window = window;
                _parts = parts;
                RuleChoice choice = null;
                if (selectedId != null)
                    foreach (var rule in parts.Rules)
                        if (rule.Id == selectedId) { choice = rule; break; }
                choice = choice ?? parts.Rules[Math.Min(defaultIndex, parts.Rules.Count - 1)];
                SelectedId = choice.Id;

                _label = Text(parts, choice.Label, 13, parts.T.Ink);
                _label.VerticalAlignment = VerticalAlignment.Center;
                _label.TextWrapping = TextWrapping.NoWrap;
                _label.TextTrimming = TextTrimming.CharacterEllipsis;
                var chevron = Glyph(parts, (char)0xE70D, 9, parts.T.Muted);
                chevron.Margin = new Thickness(8, 0, 0, 0);
                var face = new DockPanel { Margin = new Thickness(10, 0, 10, 0) };
                DockPanel.SetDock(chevron, Dock.Right);
                face.Children.Add(chevron);
                face.Children.Add(_label);

                Shell = BareButton(parts, face);
                Shell.Height = 32;
                Shell.Background = Brush(parts.T.Paper);
                Shell.BorderBrush = Brush(parts.T.LineStrong);
                Shell.BorderThickness = new Thickness(1);
                Shell.HorizontalAlignment = HorizontalAlignment.Stretch;
                Hover(Shell, () => _parts.T.Paper, parts.T.Surface);

                _popup = new Popup
                {
                    PlacementTarget = Shell,
                    Placement = PlacementMode.Bottom,
                    StaysOpen = false,
                    AllowsTransparency = true,
                    VerticalOffset = 4,
                };
                Shell.Click += (s, e) => Open();
            }

            private void Open()
            {
                var items = new StackPanel();
                foreach (var rule in _parts.Rules)
                {
                    var selected = rule.Id == SelectedId;
                    var rail = new Border
                    {
                        Width = 3,
                        Background = selected ? Brush(_parts.Accent) : Brushes.Transparent,
                    };
                    var label = Text(_parts, rule.Label, 13, _parts.T.Ink);
                    label.VerticalAlignment = VerticalAlignment.Center;
                    label.Margin = new Thickness(9, 0, 12, 0);
                    var line = new DockPanel();
                    line.Children.Add(rail);
                    line.Children.Add(label);

                    var item = BareButton(_parts, line, radius: 0);
                    item.Height = 32;
                    item.Background = selected ? Brush(_parts.T.Selection) : Brushes.Transparent;
                    if (!selected)
                        Hover(item, () => null, _parts.T.Surface);
                    var captured = rule;
                    item.Click += (s, e) =>
                    {
                        SelectedId = captured.Id;
                        _label.Text = captured.Label;
                        _popup.IsOpen = false;
                        Refresh(_window);
                    };
                    items.Children.Add(item);
                }
                _popup.Child = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = Brush(_parts.T.Paper),
                    BorderBrush = Brush(_parts.T.Line),
                    BorderThickness = new Thickness(1),
                    MinWidth = Math.Max(Shell.ActualWidth, 240),
                    Margin = new Thickness(0, 0, 12, 12),
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 12, ShadowDepth = 2, Opacity = 0.14, Direction = 270,
                    },
                    Child = items,
                };
                _popup.IsOpen = true;
            }
        }

        private static void AddRuleRow(Window window, Parts parts, string selectedId = null)
        {
            var dropdown = new Dropdown(window, parts, parts.RuleRows.Children.Count, selectedId);

            // The chain is genuinely ordered, so rows carry a plain number.
            var ordinal = TabularNumber(parts, "", 12, parts.T.Muted);
            ordinal.Width = 18;
            ordinal.VerticalAlignment = VerticalAlignment.Center;

            var remove = BareButton(parts, Glyph(parts, (char)0xE711, 9, parts.T.Muted));
            remove.Width = 28;
            remove.Height = 28;
            remove.ToolTip = "Remove this rule";
            remove.Margin = new Thickness(6, 0, 0, 0);
            remove.VerticalAlignment = VerticalAlignment.Center;
            Hover(remove, () => null, parts.T.Surface);

            var rowLine = new DockPanel();
            rowLine.Children.Add(ordinal);
            DockPanel.SetDock(remove, Dock.Right);
            rowLine.Children.Add(remove);
            rowLine.Children.Add(dropdown.Shell);

            var inner = new StackPanel();
            inner.Children.Add(rowLine);
            var toleranceSlot = new StackPanel();
            inner.Children.Add(toleranceSlot);

            var row = new Border
            {
                Margin = new Thickness(6, 0, 0, 8),
                Tag = dropdown,
                Child = inner,
            };
            remove.Click += (s, e) =>
            {
                parts.RuleRows.Children.Remove(row);
                Refresh(window);
            };

            parts.RuleRows.Children.Add(row);
        }

        private static Dropdown DropdownOf(UIElement ruleRow) => (Dropdown)((Border)ruleRow).Tag;

        private static StackPanel ToleranceSlotOf(UIElement ruleRow) =>
            (StackPanel)((StackPanel)((Border)ruleRow).Child).Children[1];

        private static TextBlock OrdinalOf(UIElement ruleRow) =>
            (TextBlock)((DockPanel)((StackPanel)((Border)ruleRow).Child).Children[0]).Children[0];

        private static void BuildTolerancePanel(Window window, Parts parts)
        {
            var input = new StackPanel { Orientation = Orientation.Horizontal };
            var label = Text(parts, "Cluster distance", 13, parts.T.Ink);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 10, 0);
            input.Children.Add(label);

            parts.Tolerance = new TextBox
            {
                Text = parts.Defaults != null
                    ? parts.Defaults.ToleranceMeters.ToString("0.###", CultureInfo.InvariantCulture)
                    : "2.0",
                FontSize = 13,
                TextAlignment = TextAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = Brush(parts.T.Ink),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 0, 4, 0),
                FocusVisualStyle = parts.FocusRing,
            };
            parts.Tolerance.TextChanged += (s, e) => Refresh(window);
            var unit = Text(parts, "m", 12, parts.T.Muted);
            unit.VerticalAlignment = VerticalAlignment.Center;
            unit.Margin = new Thickness(0, 0, 8, 0);
            var fieldRow = new DockPanel { Width = 96, Height = 30 };
            DockPanel.SetDock(unit, Dock.Right);
            fieldRow.Children.Add(unit);
            fieldRow.Children.Add(parts.Tolerance);
            parts.ToleranceShell = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderBrush = Brush(parts.T.LineStrong),
                BorderThickness = new Thickness(1),
                Background = Brush(parts.T.Paper),
                VerticalAlignment = VerticalAlignment.Center,
                Child = fieldRow,
            };
            input.Children.Add(parts.ToleranceShell);

            parts.TolerancePanel = new StackPanel { Margin = new Thickness(18, 8, 0, 4) };
            parts.TolerancePanel.Children.Add(input);
            var helper = Text(parts, "Clashes closer than this join the same group.", 12, parts.T.Muted);
            helper.Margin = new Thickness(0, 4, 0, 0);
            parts.TolerancePanel.Children.Add(helper);
            parts.ToleranceWarn = Text(parts, "Enter a distance greater than 0.", 12, parts.T.Error);
            parts.ToleranceWarn.Margin = new Thickness(0, 4, 0, 0);
            parts.ToleranceWarn.Visibility = Visibility.Collapsed;
            parts.TolerancePanel.Children.Add(parts.ToleranceWarn);
        }

        private static FrameworkElement BuildKeepExistingRow(Window window, Parts parts)
        {
            parts.ToggleSlide = new TranslateTransform(20, 0);
            parts.ToggleThumb = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0),
                RenderTransform = parts.ToggleSlide,
            };
            parts.ToggleShell = new Border
            {
                Width = 40,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 10, 0),
                Child = parts.ToggleThumb,
            };
            parts.ToggleShell.MouseLeftButtonUp += (s, e) =>
            {
                parts.KeepExisting = !parts.KeepExisting;
                Refresh(window);
            };

            var labels = new StackPanel();
            labels.Children.Add(Text(parts, "Keep existing groups", 13, parts.T.Ink));
            var caption = Text(parts,
                "Groups you already made stay untouched. Only loose clashes get grouped.",
                12, parts.T.Muted);
            caption.Margin = new Thickness(0, 2, 0, 0);
            labels.Children.Add(caption);

            var row = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(parts.ToggleShell);
            row.Children.Add(labels);
            return row;
        }

        private static void PaintToggle(Parts parts)
        {
            if (parts.KeepExisting)
            {
                parts.ToggleShell.Background = Brush(parts.Accent);
                parts.ToggleShell.BorderThickness = new Thickness(0);
                parts.ToggleThumb.Background = Brushes.White;
            }
            else
            {
                parts.ToggleShell.Background = Brushes.Transparent;
                parts.ToggleShell.BorderBrush = Brush(parts.T.LineStrong);
                parts.ToggleShell.BorderThickness = new Thickness(1);
                parts.ToggleThumb.Background = Brush(parts.T.Muted);
            }
            // State feedback only: 120ms ease-out, per the design system's motion rule.
            parts.ToggleSlide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(parts.KeepExisting ? 20 : 0, TimeSpan.FromMilliseconds(120))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }

        // ---- footer ------------------------------------------------------------

        private static FrameworkElement BuildFooter(Window window, Parts parts)
        {
            var bar = new DockPanel();

            var ungroupLabel = Text(parts, "Ungroup", 13, parts.T.Error);
            ungroupLabel.HorizontalAlignment = HorizontalAlignment.Center;
            ungroupLabel.VerticalAlignment = VerticalAlignment.Center;
            parts.Ungroup = BareButton(parts, ungroupLabel);
            parts.Ungroup.Height = 32;
            parts.Ungroup.Padding = new Thickness(14, 0, 14, 0);
            parts.Ungroup.BorderThickness = new Thickness(1);
            parts.Ungroup.MouseEnter += (s, e) =>
            {
                parts.Ungroup.Background = Brush(parts.T.ErrorWashBg);
                parts.Ungroup.BorderBrush = Brush(parts.T.ErrorWashLine);
            };
            parts.Ungroup.MouseLeave += (s, e) =>
            {
                parts.Ungroup.Background = Brushes.Transparent;
                parts.Ungroup.BorderBrush = Brushes.Transparent;
            };
            parts.Ungroup.Click += (s, e) =>
            {
                if (!Dialogs.Confirm(
                    "Remove the groups this tool previously created in the selected "
                    + "tests?\n\nGroups you made by hand are kept, and no clash is renamed.",
                    "Clash Grouper"))
                    return;
                parts.Result = ConfigFrom(parts);
                parts.Result.Action = "ungroup";
                window.DialogResult = true;
            };
            DockPanel.SetDock(parts.Ungroup, Dock.Left);
            bar.Children.Add(parts.Ungroup);

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            var cancelLabel = Text(parts, "Cancel", 13, parts.T.Ink);
            cancelLabel.HorizontalAlignment = HorizontalAlignment.Center;
            cancelLabel.VerticalAlignment = VerticalAlignment.Center;
            // Held in Parts so SetInteractive can lock it during a preview: the
            // dispatcher pumping means a click really can land mid-computation.
            parts.CancelDialog = BareButton(parts, cancelLabel);
            var cancel = parts.CancelDialog;
            cancel.Height = 32;
            cancel.MinWidth = 84;
            cancel.Background = Brush(parts.T.Paper);
            cancel.BorderBrush = Brush(parts.T.LineStrong);
            cancel.BorderThickness = new Thickness(1);
            cancel.Margin = new Thickness(0, 0, 8, 0);
            Hover(cancel, () => parts.T.Paper, parts.T.Surface);
            cancel.Click += (s, e) => window.Close();
            right.Children.Add(cancel);

            var applyLabel = Text(parts, "Apply grouping", 13, Colors.White);
            applyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            applyLabel.VerticalAlignment = VerticalAlignment.Center;
            parts.Apply = BareButton(parts, applyLabel);
            parts.Apply.Height = 32;
            parts.Apply.MinWidth = 140;
            parts.Apply.Padding = new Thickness(16, 0, 16, 0);
            parts.Apply.Background = Brush(parts.Accent);
            parts.Apply.BorderBrush = Brush(parts.Accent);
            parts.Apply.BorderThickness = new Thickness(1);
            parts.Apply.IsEnabledChanged += (s, e) => PaintApply(parts);
            parts.Apply.Click += (s, e) =>
            {
                parts.Result = ConfigFrom(parts);
                parts.Result.Action = "apply";
                window.DialogResult = true;
            };
            right.Children.Add(parts.Apply);
            bar.Children.Add(right);
            return bar;
        }

        private static void PaintApply(Parts parts)
        {
            var enabled = parts.Apply.IsEnabled;
            parts.Apply.Background = enabled ? Brush(parts.Accent) : Brush(parts.T.Surface);
            parts.Apply.BorderBrush = enabled ? Brush(parts.Accent) : Brush(parts.T.Line);
            ((TextBlock)parts.Apply.Content).Foreground =
                enabled ? Brushes.White : Brush(parts.T.Muted);
        }

        // ---- behavior ----------------------------------------------------------

        private static GrouperConfig ConfigFrom(Parts parts)
        {
            var config = new GrouperConfig
            {
                Smart = parts.Smart,
                KeepExisting = parts.KeepExisting,
            };
            foreach (var row in parts.TestRows)
                if (row.Checked)
                    config.TestIndexes.Add(row.Index);
            foreach (UIElement row in parts.RuleRows.Children)
                config.RuleIds.Add(DropdownOf(row).SelectedId);
            if (parts.Tolerance != null
                && double.TryParse(parts.Tolerance.Text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var tolerance)
                && tolerance > 0)
                config.ToleranceMeters = tolerance;
            return config;
        }

        private static void Refresh(Window window)
        {
            var parts = PartsOf(window);
            if (!parts.Loaded || parts.Busy) return;

            var config = ConfigFrom(parts);
            PaintSegments(parts);
            PaintToggle(parts);
            foreach (var row in parts.TestRows) PaintTestRow(parts, row);
            RefreshTestList(parts);

            // Counted across every row, not just the visible ones: a filter
            // hides rows, it does not deselect them, so this has to keep
            // reporting the truth while one is active.
            var ticked = config.TestIndexes.Count;
            parts.TestsDisc.Summary.Text = parts.Tests.Count == 0
                ? "none found"
                : string.Format(CultureInfo.InvariantCulture,
                    "{0} of {1} selected", ticked, parts.TestRows.Count);
            parts.ModeDisc.Summary.Text = config.Smart
                ? "Smart"
                : string.Format(CultureInfo.InvariantCulture,
                    "{0} rule{1}", config.RuleIds.Count, config.RuleIds.Count == 1 ? "" : "s");

            // Auto-expand only when a section needs attention; never auto-close.
            if (ticked == 0 && parts.Tests.Count > 0) parts.TestsDisc.Open = true;
            if (!config.Smart && config.RuleIds.Count == 0) parts.ModeDisc.Open = true;
            PaintDisclosure(parts.TestsDisc);
            PaintDisclosure(parts.ModeDisc);

            RefreshRulesArea(parts, config);
            // Really disable it rather than dimming it: a 0.55 opacity section
            // stayed clickable and dropped its text below the contrast floor.
            parts.ModeSection.IsEnabled = ticked > 0 || parts.Tests.Count == 0;
            RefreshPreview(parts, config);
        }

        private static void RefreshRulesArea(Parts parts, GrouperConfig config)
        {
            var custom = !config.Smart;
            parts.RulesArea.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            parts.EmptyChainNote.Visibility =
                custom && parts.RuleRows.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            for (var i = 0; i < parts.RuleRows.Children.Count; i++)
                OrdinalOf(parts.RuleRows.Children[i]).Text =
                    (i + 1).ToString(CultureInfo.InvariantCulture) + ".";

            // The tolerance panel nests under the FIRST proximity rule row.
            StackPanel target = null;
            if (custom)
                foreach (UIElement row in parts.RuleRows.Children)
                    if (DropdownOf(row).SelectedId == "proximity")
                    {
                        target = ToleranceSlotOf(row);
                        break;
                    }
            var current = parts.TolerancePanel.Parent as Panel;
            if (!ReferenceEquals(current, target))
            {
                current?.Children.Remove(parts.TolerancePanel);
                target?.Children.Add(parts.TolerancePanel);
            }

            var valid = double.TryParse(parts.Tolerance.Text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var tolerance) && tolerance > 0;
            parts.ToleranceWarn.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            parts.ToleranceShell.BorderBrush =
                valid ? Brush(parts.T.LineStrong) : Brush(parts.T.Error);
        }

        // ---- test accessors ----------------------------------------------------

        public static int TestCheckCountOf(Window window) => PartsOf(window).TestRows.Count;
        public static bool IsSmartOf(Window window) => PartsOf(window).Smart;
        public static bool TestsSectionOpenOf(Window window) => PartsOf(window).TestsDisc.Open;
        public static bool ModeSectionOpenOf(Window window) => PartsOf(window).ModeDisc.Open;
        public static int RuleRowCountOf(Window window) => PartsOf(window).RuleRows.Children.Count;
        public static string MessageOf(Window window) => PartsOf(window).Message.Text;
        public static string StatTextOf(Window window) =>
            PartsOf(window).StatFrom.Text + ">" + PartsOf(window).StatTo.Text;
        public static bool ApplyEnabledOf(Window window) => PartsOf(window).Apply.IsEnabled;
        public static int PreviewRowCountOf(Window window) => PartsOf(window).PreviewList.Children.Count;
        public static GrouperConfig ConfigOf(Window window) => ConfigFrom(PartsOf(window));
        public static string TestCountTextOf(Window window, int index) =>
            PartsOf(window).TestRows[index].CountText.Text;
        public static string TestCountTooltipOf(Window window, int index) =>
            PartsOf(window).TestRows[index].CountText.ToolTip as string;
        public static bool TestCheckedOf(Window window, int index) =>
            PartsOf(window).TestRows[index].Checked;
        public static bool SelectionTouchedOf(Window window) =>
            PartsOf(window).SelectionTouched;
        public static string TestsSummaryOf(Window window) =>
            PartsOf(window).TestsDisc.Summary.Text;

        /// <summary>The rows the list is showing, in the order it shows them.
        /// Names rather than indexes, so a test that asserts an order cannot
        /// accidentally pass by reading parts.TestRows instead.</summary>
        public static string[] VisibleTestNamesOf(Window window)
        {
            var rows = PartsOf(window).VisibleRows;
            var names = new string[rows.Count];
            for (var i = 0; i < rows.Count; i++) names[i] = rows[i].Row.Name;
            return names;
        }

        /// <summary>The row's number cells in the order they read on screen.
        /// Both are docked Right, where the FIRST child added is the rightmost,
        /// so reading order is the reverse of child order: this derives it from
        /// the live docking rather than restating what the builder wrote.
        /// </summary>
        public static string[] TestNumbersInReadingOrderOf(Window window, int index)
        {
            var panel = (DockPanel)PartsOf(window).TestRows[index].Shell.Content;
            var cells = new List<string>();
            foreach (UIElement child in panel.Children)
                if (DockPanel.GetDock(child) == Dock.Right
                    && child.Visibility == Visibility.Visible
                    && child is TextBlock text)
                    cells.Add(text.Text);
            cells.Reverse();
            return cells.ToArray();
        }

        /// <summary>The filter's own line as the user can read it: empty unless
        /// a filter is active, which is the only time it is on screen.</summary>
        public static string FilterNoteOf(Window window)
        {
            var note = PartsOf(window).FilterNote;
            return note != null && note.Visibility == Visibility.Visible ? note.Text : "";
        }

        public static string SortLabelOf(Window window) =>
            ((TextBlock)PartsOf(window).SortToggle.Content).Text;

        /// <summary>Types into the filter box. TextChanged runs Refresh, which is
        /// the path a real keystroke takes; the explicit Refresh only covers
        /// setting the same text twice, which fires no event.</summary>
        public static void FilterTestsForTest(Window window, string text)
        {
            PartsOf(window).Filter.Text = text ?? "";
            Refresh(window);
        }

        public static void SelectAllVisibleForTest(Window window) =>
            SetVisibleChecked(window, PartsOf(window), true);

        public static void SelectNoneVisibleForTest(Window window) =>
            SetVisibleChecked(window, PartsOf(window), false);

        public static void ToggleSortForTest(Window window) =>
            ToggleSort(window, PartsOf(window));

        /// <summary>0 on a zero-test document, which has no list at all.</summary>
        public static double TestListMaxHeightOf(Window window)
        {
            var scroll = PartsOf(window).TestScroll;
            return scroll != null ? scroll.MaxHeight : 0;
        }

        public static double TestListScrollOf(Window window) =>
            PartsOf(window).TestScroll.VerticalOffset;

        public static void ScrollTestListForTest(Window window, double offset) =>
            PartsOf(window).TestScroll.ScrollToVerticalOffset(offset);

        /// <summary>Whether the filter box is live. IsEnabled is the effective
        /// value, so this is false whenever an ancestor is disabled: it reports
        /// what the user can actually reach, which is the point during a preview.
        /// </summary>
        public static bool FilterEnabledOf(Window window) =>
            PartsOf(window).Filter.IsEnabled;

        public static int TestListRenderCountOf(Window window) =>
            PartsOf(window).ListRenders;

        public static void SetSmartForTest(Window window, bool smart)
        {
            PartsOf(window).Smart = smart;
            Refresh(window);
        }

        public static void SetTestCheckedForTest(Window window, int index, bool value)
        {
            PartsOf(window).TestRows[index].Checked = value;
            Refresh(window);
        }

        /// <summary>Types into the tolerance box. TextChanged runs Refresh, which
        /// is the path a real keystroke takes.</summary>
        public static void SetToleranceForTest(Window window, string text) =>
            PartsOf(window).Tolerance.Text = text;

        public static void AddRuleRowForTest(Window window)
        {
            AddRuleRow(window, PartsOf(window));
            Refresh(window);
        }
    }
}
