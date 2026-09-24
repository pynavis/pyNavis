using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using PyNavis.Runtime.Output;

namespace PyNavis.Runtime.Forms
{
    /// <summary>
    /// The pyNavis design system v2, in code: one shared toolkit every window and
    /// dialog is built from, matching docs/design-system. Neutral grays and bold
    /// group frames carry structure; the Windows accent appears only on the primary
    /// action, the selection rail and the focus ring; surfaces are solid (no glass,
    /// no gradient, no tint fills); numbers are muted tabular text; controls are the
    /// native affordances users already know.
    ///
    /// Everything is public static so IronPython scripts (the console) build on the
    /// same primitives as the C# forms.
    /// </summary>
    public static class DesignSystem
    {
        // ---- palette (foundations/colors) --------------------------------------

        public sealed class Tokens
        {
            public Color Paper, Surface, Line, LineStrong, FrameLine, Ink, Muted, Selection,
                Error, ErrorWashBg, ErrorWashLine, Success, Info, Warning;

            public static Tokens For(bool dark) => dark
                ? new Tokens
                {
                    Paper = C(0x20, 0x20, 0x20), Surface = C(0x2B, 0x2B, 0x2B),
                    Line = C(0x3D, 0x3D, 0x3D), LineStrong = C(0x4D, 0x4D, 0x4D),
                    FrameLine = C(0x6E, 0x6E, 0x6E),
                    Ink = C(0xE8, 0xE8, 0xE8), Muted = C(0x9A, 0x9A, 0x9A),
                    Selection = C(0x33, 0x33, 0x33), Error = C(0xFF, 0x7B, 0x72),
                    ErrorWashBg = C(0x3B, 0x2A, 0x2B), ErrorWashLine = C(0x6E, 0x3E, 0x41),
                    Success = C(0x4C, 0xC4, 0x4C), Info = C(0x4C, 0xC2, 0xFF),
                    Warning = C(0xFF, 0xC8, 0x3D),
                }
                : new Tokens
                {
                    Paper = C(0xFF, 0xFF, 0xFF), Surface = C(0xF7, 0xF7, 0xF7),
                    Line = C(0xE0, 0xE0, 0xE0), LineStrong = C(0xC9, 0xC9, 0xC9),
                    FrameLine = C(0xA6, 0xA6, 0xA6),
                    Ink = C(0x1A, 0x1A, 0x1A), Muted = C(0x6B, 0x6B, 0x6B),
                    Selection = C(0xF0, 0xF0, 0xF0), Error = C(0xC5, 0x0F, 0x1F),
                    ErrorWashBg = C(0xFD, 0xF3, 0xF4), ErrorWashLine = C(0xE8, 0xC4, 0xC8),
                    Success = C(0x0F, 0x7B, 0x0F), Info = C(0x00, 0x78, 0xD4),
                    Warning = C(0x9D, 0x5D, 0x00),
                };

            /// <summary>Tokens for the host's current light/dark theme.</summary>
            public static Tokens Current => For(PyNavisTheme.IsDark);

            private static Color C(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);
        }

        public static Color Accent => FluentChrome.AccentColor();

        public static SolidColorBrush Brush(Color color) => new SolidColorBrush(color);

        // ---- type (foundations/type) -------------------------------------------

        public static TextBlock Text(string text, double size, Color color, FontWeight? weight = null) =>
            new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = weight ?? FontWeights.Normal,
                Foreground = Brush(color),
                TextWrapping = TextWrapping.Wrap,
            };

        /// <summary>Right-aligning tabular figures for any number the eye scans down a column.</summary>
        public static TextBlock TabularNumber(string text, double size, Color color)
        {
            var block = Text(text, size, color);
            block.TextWrapping = TextWrapping.NoWrap;
            System.Windows.Documents.Typography.SetNumeralAlignment(
                block, FontNumeralAlignment.Tabular);
            return block;
        }

        public static TextBlock Glyph(char glyph, double size, Color color) => new TextBlock
        {
            Text = glyph.ToString(),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = size,
            Foreground = Brush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // ---- focus ring (rules: keyboard focus is always visible) --------------

        public static Style FocusRing(Color accent)
        {
            var rect = new FrameworkElementFactory(typeof(Rectangle));
            rect.SetValue(Shape.StrokeProperty, Brush(accent));
            rect.SetValue(Shape.StrokeThicknessProperty, 2.0);
            rect.SetValue(Rectangle.RadiusXProperty, 4.0);
            rect.SetValue(Rectangle.RadiusYProperty, 4.0);
            rect.SetValue(FrameworkElement.MarginProperty, new Thickness(-3));
            var template = new ControlTemplate { VisualTree = rect };
            var style = new Style(typeof(Control));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        // ---- buttons (components/buttons) --------------------------------------

        /// <summary>
        /// Rounded button chrome; callers paint hover. Content is centred, which
        /// is right for ordinary buttons and wrong for full-width rows and
        /// dropdown faces: those need stretchContent, or their label sits in the
        /// middle of the row instead of at its start.
        /// </summary>
        public static ControlTemplate ButtonChrome(CornerRadius radius, bool stretchContent = false)
        {
            var border = new FrameworkElementFactory(typeof(Border), "border");
            border.SetValue(Border.CornerRadiusProperty, radius);
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty,
                stretchContent ? HorizontalAlignment.Stretch : HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty,
                stretchContent ? VerticalAlignment.Stretch : VerticalAlignment.Center);
            border.AppendChild(content);
            return new ControlTemplate(typeof(Button)) { VisualTree = border };
        }

        /// <summary>
        /// The readable foreground on an accent fill. White is not automatic: a
        /// light Windows accent (yellow, lime) makes white text unreadable, so
        /// the choice follows relative luminance.
        /// </summary>
        public static Color OnAccent(Color accent) =>
            Output.ThemeMath.IsDarkColor(accent.R, accent.G, accent.B)
                ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A);

        private static Button BaseButton(Tokens t, string label, Color textColor, Action onClick)
        {
            var button = new Button
            {
                Content = label,
                FontSize = 14,
                Height = 32,
                MinWidth = 84,
                Padding = new Thickness(16, 0, 16, 0),
                Foreground = Brush(textColor),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Template = ButtonChrome(new CornerRadius(4)),
                FocusVisualStyle = FocusRing(Accent),
            };
            if (onClick != null) button.Click += (s, e) => onClick();
            EnableDisabledState(button, t, textColor);
            EnablePressedState(button);
            return button;
        }

        /// <summary>
        /// Pressed feedback: darken whatever the button is currently painted,
        /// and put it back on release. Works for every weight because it reads
        /// the live brush rather than assuming a palette, and it stays out of
        /// the way of the hover handlers, which repaint on the next MouseEnter.
        /// </summary>
        private static void EnablePressedState(Button button)
        {
            button.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (!button.IsEnabled) return;
                if (!(button.Background is SolidColorBrush live)) return;
                button.Resources["restFill"] = button.Background;
                button.Background = live.Color.A == 0
                    ? (Brush)new SolidColorBrush(Color.FromArgb(28, 0, 0, 0))
                    : Brush(Darken(live.Color, 0.12));
            };
            button.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (button.Resources["restFill"] is Brush rest) button.Background = rest;
            };
            button.MouseLeave += (s, e) =>
            {
                if (button.Resources["restFill"] is Brush rest && button.IsEnabled)
                    button.Background = rest;
            };
        }

        private static Color Darken(Color color, double amount)
        {
            byte Down(byte channel) => (byte)Math.Max(0, channel - channel * amount);
            return Color.FromRgb(Down(color.R), Down(color.G), Down(color.B));
        }

        /// <summary>
        /// Paints the disabled state. Necessary because these buttons override
        /// Template and set Background/Foreground locally, which leaves WPF's own
        /// disabled trigger with nothing to do: without this a disabled button
        /// still renders live, hand cursor and all. Hover handlers check
        /// IsEnabled, so a disabled button never lights up under the pointer.
        /// </summary>
        private static void EnableDisabledState(Button button, Tokens t, Color textColor)
        {
            button.IsEnabledChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                {
                    button.Background = button.Tag is Brush live ? live : button.Background;
                    button.BorderBrush = button.Resources["restBorder"] as Brush ?? button.BorderBrush;
                    button.Foreground = Brush(textColor);
                    button.Cursor = System.Windows.Input.Cursors.Hand;
                }
                else
                {
                    button.Tag = button.Background;
                    button.Resources["restBorder"] = button.BorderBrush;
                    button.Background = Brush(t.Surface);
                    button.BorderBrush = Brush(t.Line);
                    button.Foreground = Brush(t.Muted);
                    button.Cursor = System.Windows.Input.Cursors.Arrow;
                }
            };
        }

        /// <summary>Accent-filled primary action. One per window.</summary>
        public static Button Primary(Tokens t, string label, Action onClick)
        {
            var accent = Accent;
            var button = BaseButton(t, label, OnAccent(accent), onClick);
            button.Background = Brush(accent);
            button.BorderBrush = Brush(accent);
            var hover = Lighten(accent, 0.08);
            button.MouseEnter += (s, e) => { if (button.IsEnabled) button.Background = Brush(hover); };
            button.MouseLeave += (s, e) => { if (button.IsEnabled) button.Background = Brush(accent); };
            return button;
        }

        /// <summary>Bordered neutral action (Cancel, Add).</summary>
        public static Button Secondary(Tokens t, string label, Action onClick)
        {
            var button = BaseButton(t, label, t.Ink, onClick);
            button.Background = Brush(t.Paper);
            button.BorderBrush = Brush(t.LineStrong);
            button.MouseEnter += (s, e) => { if (button.IsEnabled) button.Background = Brush(t.Surface); };
            button.MouseLeave += (s, e) => { if (button.IsEnabled) button.Background = Brush(t.Paper); };
            return button;
        }

        /// <summary>Borderless toolbar action (Copy, Clear, Save, Reload).</summary>
        public static Button Quiet(Tokens t, string label, Action onClick)
        {
            var button = BaseButton(t, label, t.Ink, onClick);
            button.MinWidth = 0;
            button.Padding = new Thickness(12, 0, 12, 0);
            button.FontSize = 13;
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.MouseEnter += (s, e) => { if (button.IsEnabled) button.Background = Brush(t.Surface); };
            button.MouseLeave += (s, e) => { if (button.IsEnabled) button.Background = Brushes.Transparent; };
            return button;
        }

        /// <summary>Destructive action: red text, red-wash hover, kept apart from the flow.</summary>
        public static Button Danger(Tokens t, string label, Action onClick)
        {
            var button = BaseButton(t, label, t.Error, onClick);
            button.FontSize = 13;
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.MouseEnter += (s, e) =>
            {
                if (!button.IsEnabled) return;
                button.Background = Brush(t.ErrorWashBg);
                button.BorderBrush = Brush(t.ErrorWashLine);
            };
            button.MouseLeave += (s, e) =>
            {
                if (!button.IsEnabled) return;
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
            };
            return button;
        }

        // ---- surfaces (components/cards) ---------------------------------------

        /// <summary>Bold group frame with the title straddling the top border,
        /// GroupBox-style; sections read at a glance, not via faint hairlines.</summary>
        public static Grid GroupFrame(Tokens t, string title, UIElement content)
        {
            var frame = new Border
            {
                BorderBrush = Brush(t.FrameLine),
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
                var label = Text(title, 12, t.Ink, FontWeights.Medium);
                label.Background = Brush(t.Paper);
                label.Padding = new Thickness(5, 0, 5, 0);
                label.Margin = new Thickness(12, 0, 0, 0);
                label.HorizontalAlignment = HorizontalAlignment.Left;
                label.VerticalAlignment = VerticalAlignment.Top;
                grid.Children.Add(label);
            }
            return grid;
        }

        /// <summary>Rounded input shell holding a borderless TextBox and an optional unit suffix.</summary>
        public static Border InputField(Tokens t, TextBox box, string suffix = null)
        {
            box.FontSize = 13;
            box.Foreground = Brush(t.Ink);
            box.Background = Brushes.Transparent;
            box.BorderThickness = new Thickness(0);
            box.VerticalContentAlignment = VerticalAlignment.Center;
            box.Padding = new Thickness(8, 0, suffix != null ? 4 : 8, 0);
            box.FocusVisualStyle = FocusRing(Accent);

            UIElement child = box;
            if (suffix != null)
            {
                var unit = Text(suffix, 12, t.Muted);
                unit.VerticalAlignment = VerticalAlignment.Center;
                unit.Margin = new Thickness(0, 0, 8, 0);
                var row = new DockPanel();
                DockPanel.SetDock(unit, Dock.Right);
                row.Children.Add(unit);
                row.Children.Add(box);
                child = row;
            }
            return new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderBrush = Brush(t.LineStrong),
                BorderThickness = new Thickness(1),
                Background = Brush(t.Paper),
                Child = child,
            };
        }

        // ---- quiet scrollbar (rules: present but never shouts) -----------------

        private const string SlimScrollXaml =
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

        /// <summary>The thin, track-less scrollbar style (Track parts are not
        /// settable through FrameworkElementFactory, so this one template is
        /// parsed - still code-only, no .xaml build files). Add it to any control's
        /// Resources under typeof(ScrollBar) to restyle its scrollbars.</summary>
        public static Style SlimScrollStyle() =>
            (Style)System.Windows.Markup.XamlReader.Parse(SlimScrollXaml);

        /// <summary>Gives a ScrollViewer the thin, quiet scrollbar.</summary>
        public static void SlimScroll(FrameworkElement scroll) =>
            scroll.Resources.Add(typeof(ScrollBar), SlimScrollStyle());

        /// <summary>A draggable column seam that looks like a 1px rule: an
        /// 8px-wide transparent GridSplitter over a hairline, so the pane on
        /// either side can be resized without the seam reading as a control.
        /// Both go straight into the given column of the grid (a GridSplitter
        /// only resizes its own parent grid). Give the column Auto width and
        /// the neighbouring columns a MinWidth so nothing can be dragged shut.</summary>
        public static void AddColumnSplitter(Grid grid, int column, Tokens t, double margin = 0)
        {
            var rule = new Border
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = Brush(t.Line),
                IsHitTestVisible = false,
                Margin = new Thickness(margin, 0, margin, 0),
            };
            Grid.SetColumn(rule, column);
            grid.Children.Add(rule);

            var splitter = new GridSplitter
            {
                Width = 8,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = false,
                Focusable = false,
                Margin = new Thickness(margin, 0, margin, 0),
            };
            Grid.SetColumn(splitter, column);
            grid.Children.Add(splitter);
        }

        /// <summary>Keeps a dragged pane inside the grid it lives in. A Grid
        /// whose columns add up to more than it was given is NOT laid out
        /// smaller: it overflows its slot and WPF clips the overflow away, so
        /// the pane on the far side loses its right-hand edge - a list loses
        /// its scrollbar and nothing says why. A pixel-width column is served
        /// before a star one, so the pane dragged wide is the one that has to
        /// give the width back. Capping it with MaxWidth rather than rewriting
        /// its Width leaves the width the user dragged intact, so the pane
        /// returns to it the moment the window is wide enough again.</summary>
        public static void KeepPaneInside(Grid grid, ColumnDefinition pane)
        {
            grid.LayoutUpdated += (s, e) =>
            {
                // The slot, not ActualWidth: an overflowing grid reports the
                // width it wanted, so measuring itself would only confirm the
                // overflow. LayoutUpdated for the same reason - the grid's own
                // size does not change when the space around it does.
                var slot = LayoutInformation.GetLayoutSlot(grid);
                var room = slot.Width - grid.Margin.Left - grid.Margin.Right;
                if (room <= 0 || double.IsInfinity(room)) return;
                foreach (var column in grid.ColumnDefinitions)
                {
                    if (ReferenceEquals(column, pane)) continue;
                    // What the column will take whatever the pane does: a seam
                    // takes what it measured, anything else at least its minimum.
                    room -= column.Width.IsAuto
                        ? column.ActualWidth
                        : Math.Max(column.MinWidth, column.Width.IsAbsolute ? column.Width.Value : 0);
                }
                var cap = Math.Max(room, pane.MinWidth);
                if (Math.Abs(cap - pane.MaxWidth) > 0.5) pane.MaxWidth = cap;
            };
        }

        // ---- internals ---------------------------------------------------------

        private static Color Lighten(Color c, double amount)
        {
            byte Up(byte v) => (byte)Math.Min(255, v + (255 - v) * amount);
            return Color.FromRgb(Up(c.R), Up(c.G), Up(c.B));
        }
    }
}
