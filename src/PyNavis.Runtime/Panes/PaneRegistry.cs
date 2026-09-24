using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Config;

namespace PyNavis.Runtime.Panes
{
    /// <summary>
    /// Which bundle owns which slot, and the live shells for the slots Navisworks has
    /// actually created.
    ///
    /// Ordering matters here: Navisworks restores previously-open dock panes during
    /// startup, which can happen BEFORE the ribbon exists and therefore before Configure
    /// has run. So CreatePane always returns a live shell immediately and Configure
    /// fills it afterwards; a shell is never rebuilt, only re-contented.
    /// </summary>
    public static class PaneRegistry
    {
        /// <summary>Slots compiled into the loader. Slots beyond this come from the satellite.</summary>
        public const int ShippedSlots = 5;

        private static readonly Dictionary<int, PaneShell> Shells = new Dictionary<int, PaneShell>();
        private static readonly Dictionary<int, DockPaneModel> Claims = new Dictionary<int, DockPaneModel>();

        /// <summary>Raised as (bundleKey, visible) whenever Navisworks shows or hides a claimed pane.</summary>
        public static event Action<string, bool> VisibleChanged;

        public static int SlotCount { get; private set; } = ShippedSlots;

        /// <summary>Called from the loader when Navisworks builds a pane's control.</summary>
        public static Control CreatePane(int slot)
        {
            PaneShell shell;
            if (!Shells.TryGetValue(slot, out shell) || shell.IsDisposed)
            {
                shell = new PaneShell(slot);
                Shells[slot] = shell;
            }
            Fill(slot, shell);
            return shell;
        }

        /// <summary>
        /// Test-only seam: pre-registers a shell for a slot so a subsequent CreatePane(slot)
        /// reuses it (the "already have a live shell" branch above) instead of constructing
        /// the production PaneShell type. Lets a test supply a PaneShell subclass that hooks
        /// SetContent to exercise the Fill reentrancy guard without a live Navisworks host.
        /// </summary>
        internal static void SeedShellForTest(int slot, PaneShell shell)
        {
            Shells[slot] = shell;
        }

        /// <summary>Called from the loader when Navisworks destroys a pane's control.</summary>
        public static void Released(int slot)
        {
            Shells.Remove(slot);
        }

        /// <summary>Called from the loader's OnVisibleChanged.</summary>
        public static void VisibilityChanged(int slot, bool visible)
        {
            DockPaneModel model;
            if (!Claims.TryGetValue(slot, out model)) return;
            if (visible) Retitle(slot, model);
            var handler = VisibleChanged;
            if (handler != null) handler(model.BundleKey, visible);
        }

        /// <summary>Boot and Reload: re-read the claims and re-content any open pane.</summary>
        public static void Configure(IReadOnlyList<ExtensionModel> extensions,
            PyNavisConfig config, string configPath)
        {
            SlotCount = VerifiedSlotCount(
                ShippedSlots + (config != null ? config.ExtraPaneSlots : 0));
            PaneSatellite.ReconcileManifest();

            var models = (extensions ?? new List<ExtensionModel>())
                .SelectMany(e => e.Tabs)
                .SelectMany(t => t.Panels)
                .SelectMany(p => p.Items.Concat(p.Slideout))
                .OfType<DockPaneModel>()
                .ToList();

            // BundleKey is relative to its OWN extension folder, not prefixed with the
            // extension's name, so two different extensions can produce the identical
            // key. PaneSlotMap.Resolve trusts uniqueness and would silently treat the
            // second bundle as the same claim as the first, crossing slot ownership (and
            // swapping which pane a saved dock layout belongs to).
            var duplicateKeys = models
                .GroupBy(m => m.BundleKey, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            foreach (var key in duplicateKeys)
                Log.Error("Two dockpane bundles share the key '" + key + "' - their extensions " +
                          "will cross slot ownership. Bundle keys must be unique.");

            var existing = config != null
                ? (IReadOnlyDictionary<string, int>)config.PaneAssignments
                : new Dictionary<string, int>();
            var resolved = PaneSlotMap.Resolve(
                models.Select(m => m.BundleKey).ToList(), existing, SlotCount);

            Claims.Clear();
            var overflow = new List<DockPaneModel>();
            var assignments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < models.Count; i++)
            {
                var slot = resolved[i].Slot;
                if (slot == 0) { overflow.Add(models[i]); continue; }
                Claims[slot] = models[i];
                assignments[models[i].BundleKey] = slot;
            }

            if (overflow.Count > 0)
                Log.Error($"{overflow.Count} dockpane(s) have no free slot ({SlotCount} available): " +
                          string.Join(", ", overflow.Select(m => m.Title)));

            // Claims that vanished stay in config so a reinstalled extension gets its
            // slot (and its docked layout) back; merge rather than replace.
            foreach (var pair in existing)
                if (!assignments.ContainsKey(pair.Key)
                    && !assignments.ContainsValue(pair.Value)
                    && pair.Value <= SlotCount)
                    assignments[pair.Key] = pair.Value;

            try
            {
                PyNavisConfig.SavePaneAssignments(configPath, assignments,
                    config != null ? config.ExtraPaneSlots : 0);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save pane assignments", ex);
            }

            foreach (var pair in Shells.ToList())
                Fill(pair.Key, pair.Value);

            Log.Info($"Panes configured: {Claims.Count} claimed of {SlotCount} slot(s).");
        }

        public static int SlotFor(string bundleKey)
        {
            foreach (var pair in Claims)
                if (string.Equals(pair.Value.BundleKey, bundleKey, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            return 0;
        }

        public static string BundleKeyFor(int slot)
        {
            DockPaneModel model;
            return Claims.TryGetValue(slot, out model) ? model.BundleKey : null;
        }

        /// <summary>
        /// True when the bundle holds a claimed slot number that has no registered plugin
        /// type yet - the state right after Panel slots generates new slots and Reload
        /// claims one of them, before the restart that actually registers the type. Distinct
        /// from genuine overflow (<c>SlotFor(bundleKey) == 0</c>), which needs more slots,
        /// not a restart that is already pending.
        /// </summary>
        public static bool IsSlotPending(string bundleKey)
        {
            var slot = SlotFor(bundleKey);
            return slot >= 1 && PluginFor(slot) == null;
        }

        public static bool IsVisible(string bundleKey)
        {
            var plugin = PluginFor(SlotFor(bundleKey));
            return plugin != null && plugin.Visible;
        }

        /// <summary>Shows or hides a claimed pane; false when the bundle has no slot.</summary>
        public static bool SetVisible(string bundleKey, bool visible)
        {
            var plugin = PluginFor(SlotFor(bundleKey));
            if (plugin == null) return false;
            plugin.Visible = visible;
            if (visible) plugin.ActivatePane();   // it may be tabbed behind another pane
            return true;
        }

        /// <summary>
        /// Test seam: whether Navisworks has a plugin registered for this slot. Null in
        /// production, where the plugin table is asked directly.
        /// </summary>
        internal static Func<int, bool> SlotProbe;

        /// <summary>
        /// How many slots actually exist, which is not the same as how many config asked
        /// for. Slots past the shipped five come from the generated satellite, and that
        /// assembly can fail to load with no error anywhere - Navisworks just does not
        /// offer the panels. Trusting config after such a failure is worse than useless: a
        /// bundle would be assigned slot 6, get no complaint about overflow, and then
        /// silently never open, because SetVisible finds no plugin behind it.
        ///
        /// Field-measured: config said 8, Navisworks had 5, and the only visible
        /// symptom was three missing entries in View > Windows.
        ///
        /// Counts upward and stops at the first gap, so a partially-loaded satellite is
        /// reported as the prefix that works rather than an optimistic total.
        /// </summary>
        private static int VerifiedSlotCount(int expected)
        {
            if (expected <= ShippedSlots) return ShippedSlots;

            var probe = SlotProbe;
            var count = ShippedSlots;
            try
            {
                for (var slot = ShippedSlots + 1; slot <= expected; slot++)
                {
                    if (!(probe != null ? probe(slot) : IsSlotRegistered(slot))) break;
                    count = slot;
                }
            }
            catch (Exception ex)
            {
                // Cannot tell: assume config is right rather than silently dropping slots
                // that may well be there.
                Log.Error("Could not verify how many panel slots Navisworks registered", ex);
                return expected;
            }

            if (count < expected)
            {
                Log.Error("Config asks for " + expected + " panel slots but Navisworks registered " +
                          count + ". PyNavisPanes.dll did not load - re-run Panel slots and restart " +
                          "Navisworks. Until then the extra panels are unavailable.");
                WarnAboutShortfall(count);
            }
            return count;
        }

        private static bool _shortfallWarned;

        /// <summary>
        /// Tells the person who asked for the slots, not just the log. Someone who clicked
        /// Panel slots and restarted has no reason to open loader.log, and the symptom
        /// otherwise is only some absent entries in a menu they may never look at.
        ///
        /// Once per process, not once per Configure: Reload calls Configure too, and the
        /// condition cannot be cleared without the restart the message asks for, so
        /// repeating it would just punish anyone reloading while they work.
        /// </summary>
        private static void WarnAboutShortfall(int actual)
        {
            if (_shortfallWarned) return;
            _shortfallWarned = true;
            try
            {
                Forms.Toast.Show("error", "Only " + actual + " panel slots are available",
                    "Navisworks did not load the extra slots. Re-run Panel slots, then restart.");
            }
            catch (Exception ex)
            {
                Log.Error("Could not warn about the panel slot shortfall", ex);
            }
        }

        /// <summary>
        /// Kept out of line so merely calling VerifiedSlotCount does not force the
        /// Navisworks API assembly to load; see SlotCaption for why that matters.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsSlotRegistered(int slot)
        {
            return Autodesk.Navisworks.Api.Application.Plugins
                .FindPlugin("PyNavis.Pane" + slot + ".PYNV") != null;
        }

        /// <summary>
        /// Gives the pane its bundle's title instead of "pyNavis Panel N". Runs on every
        /// show because Navisworks calls OnVisibleChanged for docking and tab changes too,
        /// and a re-docked pane can come back with the host's caption; PaneShell.Retitle is
        /// a cheap no-op once the title is already right. Never throws - the caller is the
        /// loader's OnVisibleChanged override.
        /// </summary>
        private static void Retitle(int slot, DockPaneModel model)
        {
            PaneShell shell;
            if (!Shells.TryGetValue(slot, out shell) || shell.IsDisposed) return;
            try
            {
                shell.Retitle(SlotCaption(slot), model.Title);
            }
            catch (Exception ex)
            {
                Log.Error("Could not retitle pane slot " + slot, ex);
            }
        }

        /// <summary>
        /// What Navisworks calls this slot, read back off the plugin record rather than
        /// reconstructed here: the DisplayName is declared in the loader's [Plugin]
        /// attributes for slots 1-5 and in generated source for the satellite's, neither
        /// of which this assembly can reference, so a copy would be free to drift.
        ///
        /// Kept out of line, like PaneSlotBase's helpers, so that merely calling Retitle
        /// does not force the Navisworks API assembly to load. The JIT resolves a method's
        /// types on entry, before any try/catch inside it runs, so an inlined API touch
        /// here would throw past the caller's guard wherever the API is absent.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string SlotCaption(int slot)
        {
            var record = Autodesk.Navisworks.Api.Application.Plugins
                .FindPlugin("PyNavis.Pane" + slot + ".PYNV");
            return record == null ? null : record.DisplayName;
        }

        private static DockPanePlugin PluginFor(int slot)
        {
            if (slot < 1) return null;
            try
            {
                var record = Autodesk.Navisworks.Api.Application.Plugins
                    .FindPlugin("PyNavis.Pane" + slot + ".PYNV");
                if (record == null) return null;
                return (record.LoadedPlugin ?? record.LoadPlugin()) as DockPanePlugin;
            }
            catch (Exception ex)
            {
                Log.Error("Could not reach pane slot " + slot, ex);
                return null;
            }
        }

        private static readonly HashSet<int> Filling = new HashSet<int>();

        /// <summary>
        /// Never throws: called both from CreatePane (must return a usable shell) and
        /// from Configure's re-content loop, which runs inside RuntimeHost.Boot's
        /// ribbon-ready block. A throw there would abort the entire ribbon build over
        /// one broken pane, so a failure here is logged and shown in the offending
        /// pane instead of propagating.
        ///
        /// Guarded against reentry: a pane's script.py runs inside this method, and
        /// "open my panel on load" naturally leads an author to call
        /// pynavis.panes.show()/__pane__.Visible = True from it, which calls back into
        /// SetVisible -> plugin.Visible -> Navisworks -> CreateControlPane -> Fill for the
        /// same slot. Whether that terminates on its own is not something either side of
        /// this codebase controls, so refuse the reentrant call outright rather than trust
        /// the host to break the cycle.
        /// </summary>
        private static void Fill(int slot, PaneShell shell)
        {
            if (!Filling.Add(slot))
            {
                Log.Error("Pane slot " + slot + " re-entered Fill while already filling - refused. " +
                          "Its script.py likely shows or hides its own panel from within its own build.");
                return;
            }
            try
            {
                DockPaneModel model;
                if (!Claims.TryGetValue(slot, out model))
                {
                    shell.ShowMessage("Panel slot " + slot + " is not in use",
                        "Install an extension with a *.dockpane bundle to fill it.");
                    // A Reload can strip a claim from a pane that is open and already
                    // retitled; leaving our caption there would label an empty slot with
                    // the removed bundle's name.
                    shell.RestoreCaption();
                    return;
                }
                shell.SetContent(PaneContentBuilder.Build(model, shell));
            }
            catch (Exception ex)
            {
                Log.Error("Could not fill pane slot " + slot, ex);
                try
                {
                    shell.ShowMessage("Panel slot " + slot + " failed to load",
                        "See the pyNavis log for details.");
                }
                catch (Exception inner)
                {
                    Log.Error("Could not show the failure message for pane slot " + slot, inner);
                }
            }
            finally
            {
                Filling.Remove(slot);
            }
        }
    }
}
