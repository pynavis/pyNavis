using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;

namespace PyNavis
{
    /// <summary>
    /// A pane slot: the compile-time half of a pyNavis dock pane. Navisworks discovers
    /// dock panes by scanning attributed types in the Plugins folder at startup, so the
    /// slots must exist here in the loader; all content comes from the runtime, exactly
    /// like ConsoleCommand delegating to RuntimeHost.
    ///
    /// Public so the generated satellite assembly (slots 6 and up) can subclass it.
    /// </summary>
    public abstract class PaneSlotBase : DockPanePlugin
    {
        // Navisworks restores previously-open panes early, possibly before
        // BootPlugin.OnLoaded; Install is idempotent, so arming it here is free.
        static PaneSlotBase()
        {
            AssemblyResolver.Install();
        }

        /// <summary>1-based slot number, matching the plugin name PyNavis.Pane&lt;N&gt;.</summary>
        protected abstract int Slot { get; }

        public override Control CreateControlPane()
        {
            LoaderLog.Info("PaneSlot" + Slot + ".CreateControlPane");
            try
            {
                var pane = Build(Slot);
                pane.CreateControl();     // the Autodesk sample forces the handle; keep it
                return pane;
            }
            catch (Exception ex)
            {
                // Not ReportFatal: a modal here would pump up to five sequential dialogs
                // during Navisworks' dock-layout restore, and a broken panel should show
                // why in its own pane, not a message box. The Label fallback does that.
                LoaderLog.Error("Pane slot " + Slot + " failed to build. " + ex);
                return new Label { Text = "Panel failed to load, see loader.log", Dock = DockStyle.Fill };
            }
        }

        public override void DestroyControlPane(Control pane)
        {
            LoaderLog.Info("PaneSlot" + Slot + ".DestroyControlPane");
            Released(Slot);
            if (pane != null) pane.Dispose();
        }

        public override void OnVisibleChanged()
        {
            // Not logged: Navisworks calls this on every dock, undock and tab switch, not
            // just open/close, so it would dominate loader.log - usually the first thing
            // read when a pane misbehaves - with noise unrelated to any actual problem.
            VisibilityChanged(Slot, Visible);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Control Build(int slot)
        {
            return Runtime.Panes.PaneRegistry.CreatePane(slot);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Released(int slot)
        {
            try { Runtime.Panes.PaneRegistry.Released(slot); } catch { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void VisibilityChanged(int slot, bool visible)
        {
            try { Runtime.Panes.PaneRegistry.VisibilityChanged(slot, visible); } catch { }
        }
    }

    [Plugin("PyNavis.Pane1", "PYNV", DisplayName = "pyNavis Panel 1", ToolTip = "pyNavis dock panel")]
    [DockPanePlugin(320, 420, AutoScroll = false, MinimumWidth = 200, MinimumHeight = 160)]
    public class PaneSlot1 : PaneSlotBase { protected override int Slot { get { return 1; } } }

    [Plugin("PyNavis.Pane2", "PYNV", DisplayName = "pyNavis Panel 2", ToolTip = "pyNavis dock panel")]
    [DockPanePlugin(320, 420, AutoScroll = false, MinimumWidth = 200, MinimumHeight = 160)]
    public class PaneSlot2 : PaneSlotBase { protected override int Slot { get { return 2; } } }

    [Plugin("PyNavis.Pane3", "PYNV", DisplayName = "pyNavis Panel 3", ToolTip = "pyNavis dock panel")]
    [DockPanePlugin(320, 420, AutoScroll = false, MinimumWidth = 200, MinimumHeight = 160)]
    public class PaneSlot3 : PaneSlotBase { protected override int Slot { get { return 3; } } }

    [Plugin("PyNavis.Pane4", "PYNV", DisplayName = "pyNavis Panel 4", ToolTip = "pyNavis dock panel")]
    [DockPanePlugin(320, 420, AutoScroll = false, MinimumWidth = 200, MinimumHeight = 160)]
    public class PaneSlot4 : PaneSlotBase { protected override int Slot { get { return 4; } } }

    [Plugin("PyNavis.Pane5", "PYNV", DisplayName = "pyNavis Panel 5", ToolTip = "pyNavis dock panel")]
    [DockPanePlugin(320, 420, AutoScroll = false, MinimumWidth = 200, MinimumHeight = 160)]
    public class PaneSlot5 : PaneSlotBase { protected override int Slot { get { return 5; } } }
}
