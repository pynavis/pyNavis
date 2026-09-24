using System;
using NavApi = Autodesk.Navisworks.Api;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace PyNavis.Runtime.Events
{
    /// <summary>
    /// The one place that touches Navisworks API events. Wire() subscribes once per
    /// session, normalizes to NavisEvent, debounces the chatty ones, and fans out to
    /// consumers (ContextGate, HookRunner, toggle refresh). UI thread only: API events
    /// arrive there and consumers must stay there because the API is not thread-safe.
    /// </summary>
    public static class NavisEvents
    {
        public const int DebounceMs = 250;

        /// <summary>All consumers hang off this one multiplexed event.</summary>
        public static event Action<NavisEventArgs> Raised;

        private static readonly PendingEvents Pending = new PendingEvents();

        private static bool _wired;
        private static NavApi.Document _doc;
        private static int _modelCount;
        private static System.Windows.Forms.Timer _selectionTimer;
        private static System.Windows.Forms.Timer _setsTimer;
        private static System.Windows.Forms.Timer _cameraTimer;

        public static void Wire()
        {
            if (_wired) return;
            _wired = true;
            _selectionTimer = MakeTimer(NavisEvent.SelectionChanged);
            _setsTimer = MakeTimer(NavisEvent.SelectionSetsChanged);
            _cameraTimer = MakeTimer(NavisEvent.CameraMoved);
            NavApp.ActiveDocumentChanged += OnActiveDocumentChanged;
            NavApp.GuiDestroying += OnGuiDestroying;
            HookDocument(NavApp.ActiveDocument);
            Log.Info("NavisEvents wired.");
        }

        public static void Unwire()
        {
            if (!_wired) return;
            _wired = false;
            NavApp.ActiveDocumentChanged -= OnActiveDocumentChanged;
            NavApp.GuiDestroying -= OnGuiDestroying;
            HookDocument(null);
            _selectionTimer.Dispose(); _selectionTimer = null;
            _setsTimer.Dispose(); _setsTimer = null;
            _cameraTimer.Dispose(); _cameraTimer = null;
        }

        /// <summary>Raises to all consumers; one failing consumer never starves the rest.</summary>
        public static void Raise(NavisEvent evt, string commandKey = null,
            string viewpointName = null)
        {
            var handlers = Raised;
            if (handlers == null) return;
            var args = new NavisEventArgs
            {
                Event = evt,
                CommandKey = commandKey,
                ViewpointName = viewpointName,
            };
            foreach (Action<NavisEventArgs> handler in handlers.GetInvocationList())
            {
                try { handler(args); }
                catch (Exception ex) { Log.Error($"Event consumer failed on '{args.Name}'", ex); }
            }
        }

        /// <summary>
        /// Raises when nothing is running, otherwise holds the event until the command
        /// finishes. Reload raises app-init from INSIDE the Reload command, and HookRunner
        /// skips hooks while the gate is held (a hook's Python must not run nested in the
        /// outer script's engine run), so a plain Raise there is silently dropped.
        /// </summary>
        public static void RaiseWhenIdle(NavisEvent evt)
        {
            if (!Execution.RunGate.IsHeld)
            {
                Raise(evt);
                return;
            }
            Pending.Add(evt);
            Log.Info($"Event '{EventNames.NameOf(evt)}' held until " +
                     $"'{Execution.RunGate.CurrentTitle}' finishes.");
        }

        /// <summary>Replays whatever RaiseWhenIdle held back. Called once the gate frees.</summary>
        public static void FlushPending()
        {
            foreach (var evt in Pending.Take()) Raise(evt);
        }

        /// <summary>Moves the document-part subscriptions from the old doc to the new one.</summary>
        private static void HookDocument(NavApi.Document doc)
        {
            if (_doc != null)
            {
                _doc.FileNameChanged -= OnFileNameChanged;
                _doc.FileSaved -= OnFileSaved;
                _doc.Models.CollectionChanged -= OnModelsChanged;
                _doc.CurrentSelection.Changed -= OnSelectionChanged;
                _doc.SelectionSets.Changed -= OnSelectionSetsChanged;
                _doc.SavedViewpoints.Changed -= OnViewpointsChanged;
                _doc.SavedViewpoints.CurrentSavedViewpointChanged -= OnViewpointRecalled;
                _doc.CurrentViewpoint.Changed -= OnCameraMoved;
            }
            _doc = doc;
            if (doc == null) return;
            _modelCount = doc.Models.Count;
            doc.FileNameChanged += OnFileNameChanged;
            doc.FileSaved += OnFileSaved;
            doc.Models.CollectionChanged += OnModelsChanged;
            doc.CurrentSelection.Changed += OnSelectionChanged;
            doc.SelectionSets.Changed += OnSelectionSetsChanged;
            doc.SavedViewpoints.Changed += OnViewpointsChanged;
            doc.SavedViewpoints.CurrentSavedViewpointChanged += OnViewpointRecalled;
            doc.CurrentViewpoint.Changed += OnCameraMoved;
        }

        private static void OnActiveDocumentChanged(object sender, EventArgs e) =>
            HookDocument(NavApp.ActiveDocument);

        private static void OnGuiDestroying(object sender, EventArgs e) =>
            Raise(NavisEvent.AppClosing);

        private static void OnFileNameChanged(object sender, EventArgs e) =>
            Raise(string.IsNullOrEmpty(_doc.FileName) ? NavisEvent.DocClosed : NavisEvent.DocOpened);

        private static void OnFileSaved(object sender, NavApi.FileSaveEventArgs e) =>
            Raise(NavisEvent.DocSaved);

        private static void OnModelsChanged(object sender, EventArgs e)
        {
            // The API gives no added/removed detail; the count delta decides.
            var count = _doc.Models.Count;
            var evt = count >= _modelCount ? NavisEvent.ModelAppended : NavisEvent.ModelRemoved;
            _modelCount = count;
            Raise(evt);
        }

        private static void OnSelectionChanged(object sender, EventArgs e)
        {
            _selectionTimer.Stop();
            _selectionTimer.Start();
        }

        private static void OnSelectionSetsChanged(object sender, NavApi.SavedItemChangedEventArgs e)
        {
            _setsTimer.Stop();
            _setsTimer.Start();
        }

        private static void OnViewpointsChanged(object sender, NavApi.SavedItemChangedEventArgs e) =>
            Raise(NavisEvent.ViewpointsChanged);

        /// <summary>
        /// A saved viewpoint was activated. Raised straight away: unlike the camera, this
        /// fires once per recall, and the name has to be read now - by the time a debounced
        /// hook ran, the user could have moved on to a different view.
        /// </summary>
        private static void OnViewpointRecalled(object sender, EventArgs e)
        {
            string name = null;
            try
            {
                var current = _doc == null ? null : _doc.SavedViewpoints.CurrentSavedViewpoint;
                if (current != null) name = current.DisplayName;
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the recalled viewpoint's name", ex);
                return;
            }

            if (!IsRecall(name)) return;
            Raise(NavisEvent.ViewpointRecalled, viewpointName: name);
        }

        /// <summary>
        /// Whether the host's CurrentSavedViewpointChanged represents an actual recall.
        /// </summary>
        /// <remarks>
        /// Field-measured: Navisworks raises this event when a viewpoint stops
        /// being current as well as when one becomes current, and "no viewpoint" is a null
        /// CurrentSavedViewpoint. That made the raw event fire in two places it should not.
        /// Orbiting away from a saved view raised a recall of nothing, so simply moving the
        /// camera ran the hook; and clicking a saved view raised twice, once clearing the
        /// old one and once setting the new, so every recall ran the hook two times.
        /// Ignoring the null half fixes both, and loses nothing: "the user is no longer on a
        /// saved viewpoint" is what camera-moved already reports.
        /// </remarks>
        internal static bool IsRecall(string viewpointName)
        {
            return !string.IsNullOrEmpty(viewpointName);
        }

        /// <summary>
        /// The view moved. Debounced like the selection events, and for a stronger reason:
        /// this fires continuously while the user orbits or walks, so raising it directly
        /// would run a hook's Python script many times a second for one gesture. Consumers
        /// therefore see where the camera came to rest, not the path it took.
        /// </summary>
        private static void OnCameraMoved(object sender, EventArgs e)
        {
            _cameraTimer.Stop();
            _cameraTimer.Start();
        }

        private static System.Windows.Forms.Timer MakeTimer(NavisEvent evt)
        {
            var timer = new System.Windows.Forms.Timer { Interval = DebounceMs };
            timer.Tick += (s, e) => { timer.Stop(); Raise(evt); };
            return timer;
        }
    }
}
