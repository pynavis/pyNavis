using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Every .xaml the shipped extension carries, loaded the way the runtime loads
    /// it, with the element names its python actually looks up.
    ///
    /// Both failures this catches are invisible until someone clicks: malformed
    /// markup throws inside XamlReader, and a renamed x:Name throws a KeyError out
    /// of `win['Thing']`. Neither shows up at ribbon build, so without this a
    /// broken settings window ships looking perfectly fine.
    ///
    /// The name lists are deliberately written out rather than scraped from the
    /// python: renaming an element SHOULD fail here and make whoever did it look
    /// at both sides.
    /// </summary>
    public class ShippedXamlTests
    {
        private static string ExtensionDir()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "extensions", "pyNavis.extension");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("shipped pyNavis.extension not found above test dir");
        }

        /// <summary>Relative path -> the names the paired script indexes by name.</summary>
        public static IEnumerable<object[]> ShippedXaml() => new List<object[]>
        {
            new object[]
            {
                @"lib\reset_speeds_settings.xaml",          // lib\speedsconfig.py
                new[] { "LinearBox", "UnitBox", "LinearCheck", "AngularBox", "AngularCheck",
                        "FovBox", "FovCheck", "OkButton", "ResetButton" }
            },
            new object[]
            {
                @"lib\element_id_settings.xaml",            // lib\elementidconfig.py
                new[] { "CategoryBox", "PropertyBox", "ZoomBox", "IsolateBox", "ClipboardBox",
                        "EveryMatchBox", "OkButton", "ResetButton" }
            },
            new object[]
            {
                @"pyNavis.tab\04_Data.panel\02_Element_IDs.stack\01_Select_by_IDs.pushbutton\layout.xaml",
                new[] { "IdsBox", "OkButton" }
            },
            new object[]
            {
                @"pyNavis.tab\Viewpoints.panel\05_Viewpoint_Tracker.dockpane\pane.xaml",
                new[] { "Status", "ViewName" }
            },
            new object[]
            {
                @"pyNavis.tab\05_AI_(beta).panel\01_Ask_AI.dockpane\pane.xaml",   // script.py adds the C# pane into Host
                new[] { "Host" }
            },
            new object[]
            {
                @"pyNavis.tab\Viewpoints.panel\06_Section_Nudge.dockpane\pane.xaml",
                new[] { "Root", "Readout", "SectionOn", "Tabs", "AdjustTab", "MoveTab",
                        "CellTop", "CellTopName", "CellTopAxis", "CellLeft", "CellLeftName", "CellLeftAxis",
                        "CellFront", "CellFrontName", "CellFrontAxis", "CellRight", "CellRightName", "CellRightAxis",
                        "CellBack", "CellBackName", "CellBackAxis", "CellBottom", "CellBottomName", "CellBottomAxis",
                        "Others", "NudgeIn", "NudgeOut",
                        "MovesWorld", "MovesScreen", "MoveLeft", "MoveRight", "MoveUp", "MoveDown", "MoveNearer", "MoveFarther",
                        "StepBox", "StepUnits", "StepHalve", "StepDouble", "Presets",
                        "FitSelection", "SectionOff" }
            },
            new object[]
            {
                @"pyNavis.tab\Viewpoints.panel\99_More.slideout\Tracker_Window.pushbutton\window.xaml",
                new[] { "Status", "ViewName" }
            },
        };

        [Theory]
        [MemberData(nameof(ShippedXaml))]
        public void EveryShippedXaml_Parses_AndCarriesTheNamesItsScriptLooksUp(
            string relative, string[] names)
        {
            var path = Path.Combine(ExtensionDir(), relative);
            Assert.True(File.Exists(path), "missing " + relative);

            OnSta(() =>
            {
                object root;
                using (var stream = File.OpenRead(path))
                    root = XamlReader.Load(stream);

                var element = Assert.IsAssignableFrom<FrameworkElement>(root);
                foreach (var name in names)
                    Assert.True(element.FindName(name) != null,
                        string.Format("{0} has no element named '{1}'", relative, name));
            });
        }

        [Fact]
        public void EveryXamlInTheExtension_IsCoveredByThisTest()
        {
            // A new .xaml that nobody listed above would otherwise ship untested,
            // which is the whole failure this class exists to stop.
            var root = ExtensionDir();
            var found = Directory.GetFiles(root, "*.xaml", SearchOption.AllDirectories);
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in ShippedXaml())
                listed.Add(Path.GetFullPath(Path.Combine(root, (string)row[0])));

            foreach (var path in found)
                Assert.True(listed.Contains(Path.GetFullPath(path)),
                    Path.GetFileName(path) + " is not listed in ShippedXaml()");
        }

        [Fact]
        public void APaneXaml_IsAPlainElement_NotAWindow()
        {
            // A dockpane's content is put straight into the panel. A Window root is
            // unwrapped as a fallback, but it is not the supported form and nothing
            // else would report it.
            var path = Path.Combine(ExtensionDir(),
                @"pyNavis.tab\Viewpoints.panel\05_Viewpoint_Tracker.dockpane\pane.xaml");

            OnSta(() =>
            {
                object root;
                using (var stream = File.OpenRead(path))
                    root = XamlReader.Load(stream);
                Assert.False(root is Window, "pane.xaml must not have a Window root");
            });
        }

        private static void OnSta(Action body)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }
    }
}
