using System;
using System.Linq;
using System.Threading;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Shortcuts editor window: STA construction smoke tests over the Build seam
    /// (never shown), with the model driven to dirty/conflict states beforehand
    /// to pin the Save button's enablement rules.
    /// </summary>
    public class ShortcutsDialogTests
    {
        private static ShortcutsModel Model()
        {
            var ext = new ExtensionModel { Name = "T", Directory = @"C:\x" };
            var tab = new TabModel { Title = "T", Id = "T" };
            ext.Tabs.Add(tab);

            var general = new PanelModel { Title = "General" };
            general.Items.Add(new PushButtonModel
            { BundleKey = "t/g/A.pushbutton", Title = "Alpha", Shortcut = "Ctrl+Shift+A", ScriptPath = "a" });
            general.Items.Add(new PushButtonModel
            { BundleKey = "t/g/B.pushbutton", Title = "Beta", ScriptPath = "b" });
            tab.Panels.Add(general);

            var memory = new PanelModel { Title = "Memory" };
            memory.Items.Add(new PushButtonModel
            { BundleKey = "t/m/C.pushbutton", Title = "Gamma", Shortcut = "Ctrl+Shift+G", ScriptPath = "c" });
            tab.Panels.Add(memory);

            return ShortcutsModel.Build(new[] { ext }, PyNavisConfig.Load(null));
        }

        [Fact]
        public void Builds_WithARecorderPerRow_AndPanelHeaders()
        {
            OnSta(() =>
            {
                var window = ShortcutsDialog.Build(Model());

                Assert.Equal("Keyboard Shortcuts", window.Title);
                Assert.Equal(3, ShortcutsDialog.RecorderCountOf(window));
                Assert.Equal(2, ShortcutsDialog.PanelHeaderCountOf(window));
            });
        }

        [Fact]
        public void Search_NarrowsTheList_AndSaysSoWhenNothingMatches()
        {
            OnSta(() =>
            {
                var window = ShortcutsDialog.Build(Model());
                Assert.Equal(3, ShortcutsDialog.RecorderCountOf(window));

                ShortcutsDialog.SearchForTest(window, "alph");
                Assert.Equal(1, ShortcutsDialog.RecorderCountOf(window));

                // no hits: the list empties and the empty state takes over
                ShortcutsDialog.SearchForTest(window, "zzz");
                Assert.Equal(0, ShortcutsDialog.RecorderCountOf(window));

                ShortcutsDialog.SearchForTest(window, "");
                Assert.Equal(3, ShortcutsDialog.RecorderCountOf(window));
            });
        }

        [Fact]
        public void Search_AlsoMatchesThePanelAndTheBoundChord()
        {
            OnSta(() =>
            {
                var window = ShortcutsDialog.Build(Model());

                ShortcutsDialog.SearchForTest(window, "memory");
                Assert.Equal(1, ShortcutsDialog.RecorderCountOf(window));

                ShortcutsDialog.SearchForTest(window, "ctrl+shift+a");
                Assert.Equal(1, ShortcutsDialog.RecorderCountOf(window));
            });
        }

        [Fact]
        public void Save_IsDisabled_WhileNothingChanged()
        {
            OnSta(() =>
            {
                var window = ShortcutsDialog.Build(Model());
                Assert.False(ShortcutsDialog.SaveEnabledOf(window));
            });
        }

        [Fact]
        public void Save_IsEnabled_AfterAnEdit()
        {
            OnSta(() =>
            {
                var model = Model();
                Assert.Null(model.TrySet("t/g/B.pushbutton", "Ctrl+Alt+B"));

                var window = ShortcutsDialog.Build(model);
                Assert.True(ShortcutsDialog.SaveEnabledOf(window));
            });
        }

        [Fact]
        public void Save_IsDisabled_WhileAConflictExists()
        {
            OnSta(() =>
            {
                var model = Model();
                Assert.Null(model.TrySet("t/m/C.pushbutton", "Ctrl+Shift+A")); // clashes with Alpha

                var window = ShortcutsDialog.Build(model);
                Assert.False(ShortcutsDialog.SaveEnabledOf(window));
                Assert.Equal(1, ShortcutsDialog.ConflictCaptionCountOf(window));
            });
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
