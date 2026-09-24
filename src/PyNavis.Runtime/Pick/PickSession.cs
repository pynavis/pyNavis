using System;

namespace PyNavis.Runtime.Pick
{
    /// <summary>
    /// One interactive point pick, as a state machine with no Navisworks in it:
    /// Idle -> Active -> Completed or Cancelled, and never back. PickService owns
    /// the Navisworks side and drives this from the tool plugin's input handlers;
    /// pynavis.pick waits on Ended.
    ///
    /// Ended fires exactly once and input after it is ignored, because both ways
    /// a pick can finish are reachable at the same moment: Escape during the
    /// MouseDown that completes it, a tool change caused by the restore that
    /// follows a completion, a second script starting its own pick. A session
    /// that could end twice would restore the tool twice and hand two answers to
    /// a script waiting for one.
    /// </summary>
    public sealed class PickSession
    {
        /// <summary>How far the hover point must move to count as changed, in
        /// model units. The pick hands back the same doubles while the mouse
        /// sits still, so anything larger is a real move and anything smaller is
        /// not worth a redraw.</summary>
        public const double HoverEpsilon = 1e-6;

        public PickSession(string prompt)
        {
            Prompt = prompt ?? "";
        }

        public string Prompt { get; private set; }

        public PickState State { get; private set; }

        /// <summary>The live hover point in world coordinates, or null.</summary>
        public double[] HoverPoint { get; private set; }

        public double[] HoverNormal { get; private set; }

        public PickSnap HoverSnap { get; private set; }

        public bool HasHover { get { return HoverPoint != null; } }

        /// <summary>The picked point, or null unless State is Completed.</summary>
        public PickHit Result { get; private set; }

        public PickCancelReason CancelReason { get; private set; }

        public bool HasEnded
        {
            get { return State == PickState.Completed || State == PickState.Cancelled; }
        }

        /// <summary>Raised once, whichever way the session ended.</summary>
        public event EventHandler Ended;

        /// <summary>Moves Idle to Active. Later calls do nothing, so a
        /// re-entrant Begin cannot revive a session that already ended.</summary>
        public void Activate()
        {
            if (State == PickState.Idle) State = PickState.Active;
        }

        /// <summary>
        /// Records where the mouse is now. True when the marker has to be
        /// redrawn: the point moved, the snap kind changed, or there was no
        /// hover before. False is the common case while the mouse sits still,
        /// and it is what keeps MouseMove from asking for a redraw per event.
        /// </summary>
        public bool Hover(double[] point, double[] normal, PickSnap snap)
        {
            if (State != PickState.Active) return false;
            if (point == null || point.Length != 3) return ClearHover();

            var changed = HoverPoint == null || HoverSnap != snap || Moved(HoverPoint, point);
            HoverPoint = new[] { point[0], point[1], point[2] };
            HoverNormal = normal == null || normal.Length != 3
                ? null : new[] { normal[0], normal[1], normal[2] };
            HoverSnap = snap;
            return changed;
        }

        /// <summary>Drops the hover; true when there was one to drop.</summary>
        public bool ClearHover()
        {
            if (HoverPoint == null) return false;
            HoverPoint = null;
            HoverNormal = null;
            HoverSnap = PickSnap.None;
            return true;
        }

        /// <summary>Ends with a point. Ignored once the session has ended.</summary>
        public void Complete(PickHit hit)
        {
            if (State != PickState.Active || hit == null) return;
            Result = hit;
            State = PickState.Completed;
            ClearHover();
            Raise();
        }

        /// <summary>Ends without a point. Ignored once the session has ended.</summary>
        public void Cancel(PickCancelReason reason)
        {
            if (HasEnded) return;
            CancelReason = reason;
            State = PickState.Cancelled;
            ClearHover();
            Raise();
        }

        private void Raise()
        {
            var handler = Ended;
            Ended = null;   // one shot: a handler that ends the session again finds nothing
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static bool Moved(double[] a, double[] b)
        {
            for (var i = 0; i < 3; i++)
                if (Math.Abs(a[i] - b[i]) > HoverEpsilon) return true;
            return false;
        }
    }
}
