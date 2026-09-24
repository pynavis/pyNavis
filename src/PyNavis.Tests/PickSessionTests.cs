using PyNavis.Runtime.Pick;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The interactive pick's pure half: the session state machine, the hover
    /// change test that decides whether a mouse move is worth a redraw, and the
    /// translation of raw PickResults bits into a snap kind. Everything that
    /// needs a view (the tool swap, the pick itself, the marker) lives in
    /// PickService and is field-checked.
    /// </summary>
    public class PickSessionTests
    {
        private static PickSession Active(string prompt = "Click a point")
        {
            var session = new PickSession(prompt);
            session.Activate();
            return session;
        }

        private static PickHit Hit(double x = 1, double y = 2, double z = 3)
        {
            return new PickHit { Point = new[] { x, y, z }, Snap = PickSnap.Vertex };
        }

        [Fact]
        public void Activate_MovesIdleToActive_AndNeverRevivesAnEndedSession()
        {
            var session = new PickSession("Click a point");
            Assert.Equal(PickState.Idle, session.State);
            Assert.Equal("Click a point", session.Prompt);

            session.Activate();
            Assert.Equal(PickState.Active, session.State);

            session.Cancel(PickCancelReason.Escape);
            session.Activate();
            Assert.Equal(PickState.Cancelled, session.State);
        }

        [Fact]
        public void Complete_EndsWithTheHit_AndRaisesEndedExactlyOnce()
        {
            var session = Active();
            var ended = 0;
            session.Ended += (s, e) => ended++;

            session.Complete(Hit());

            Assert.Equal(PickState.Completed, session.State);
            Assert.True(session.HasEnded);
            Assert.Equal(new[] { 1.0, 2.0, 3.0 }, session.Result.Point);
            Assert.Equal(PickCancelReason.None, session.CancelReason);
            Assert.Equal(1, ended);

            // Escape landing in the same breath as the click must not fire again.
            session.Cancel(PickCancelReason.Escape);
            session.Complete(Hit(9, 9, 9));
            Assert.Equal(1, ended);
            Assert.Equal(PickState.Completed, session.State);
            Assert.Equal(new[] { 1.0, 2.0, 3.0 }, session.Result.Point);
        }

        [Theory]
        [InlineData(PickCancelReason.Escape)]
        [InlineData(PickCancelReason.RightClick)]
        [InlineData(PickCancelReason.ToolChanged)]
        [InlineData(PickCancelReason.Superseded)]
        [InlineData(PickCancelReason.Error)]
        public void Cancel_RecordsItsReason_AndLeavesNoResult(PickCancelReason reason)
        {
            var session = Active();
            var ended = 0;
            session.Ended += (s, e) => ended++;

            session.Cancel(reason);

            Assert.Equal(PickState.Cancelled, session.State);
            Assert.Equal(reason, session.CancelReason);
            Assert.Null(session.Result);
            Assert.Equal(1, ended);

            // A second cancel (the tool restore firing Changed, say) is ignored.
            session.Cancel(PickCancelReason.ToolChanged);
            Assert.Equal(reason, session.CancelReason);
            Assert.Equal(1, ended);
        }

        [Fact]
        public void Complete_IgnoresANullHit_AndInputBeforeActivate()
        {
            var idle = new PickSession("p");
            Assert.False(idle.Hover(new[] { 1.0, 2.0, 3.0 }, null, PickSnap.Vertex));
            Assert.False(idle.HasHover);
            idle.Complete(Hit());
            Assert.Equal(PickState.Idle, idle.State);

            var session = Active();
            session.Complete(null);
            Assert.Equal(PickState.Active, session.State);
        }

        [Fact]
        public void Hover_ReportsOnlyRealChanges_SoMouseMovesDoNotStormTheRedraw()
        {
            var session = Active();

            Assert.True(session.Hover(new[] { 1.0, 2.0, 3.0 }, new[] { 0.0, 0.0, 1.0 }, PickSnap.None));
            Assert.True(session.HasHover);
            Assert.Equal(new[] { 0.0, 0.0, 1.0 }, session.HoverNormal);

            // Same pixel, same doubles: nothing to redraw.
            Assert.False(session.Hover(new[] { 1.0, 2.0, 3.0 }, null, PickSnap.None));
            // Under the epsilon is the pick handing back its own numbers, not a move.
            Assert.False(session.Hover(new[] { 1.0 + 1e-9, 2.0, 3.0 }, null, PickSnap.None));
            // A real move, and a snap change at the same point, both redraw.
            Assert.True(session.Hover(new[] { 1.5, 2.0, 3.0 }, null, PickSnap.None));
            Assert.True(session.Hover(new[] { 1.5, 2.0, 3.0 }, null, PickSnap.Vertex));
            Assert.Equal(PickSnap.Vertex, session.HoverSnap);
        }

        [Fact]
        public void ClearHover_OnlyReportsWhenThereWasOne_AndCompletionDropsIt()
        {
            var session = Active();
            Assert.False(session.ClearHover());

            session.Hover(new[] { 1.0, 2.0, 3.0 }, null, PickSnap.Edge);
            Assert.True(session.ClearHover());
            Assert.False(session.HasHover);
            Assert.Equal(PickSnap.None, session.HoverSnap);

            // A malformed point is a cleared hover, not a half-set one.
            session.Hover(new[] { 1.0, 2.0, 3.0 }, null, PickSnap.Edge);
            Assert.True(session.Hover(new[] { 1.0, 2.0 }, null, PickSnap.Edge));
            Assert.False(session.HasHover);

            session.Hover(new[] { 4.0, 5.0, 6.0 }, null, PickSnap.Vertex);
            session.Complete(Hit());
            Assert.False(session.HasHover);
        }

        [Fact]
        public void Hover_AfterEnding_IsIgnored()
        {
            var session = Active();
            session.Cancel(PickCancelReason.Escape);

            Assert.False(session.Hover(new[] { 1.0, 2.0, 3.0 }, null, PickSnap.Vertex));
            Assert.False(session.HasHover);
        }

        [Theory]
        [InlineData(0, PickSnap.None)]
        [InlineData(128, PickSnap.None)]              // Contained on its own is a plain face
        [InlineData(16, PickSnap.Vertex)]             // Vertex0
        [InlineData(64, PickSnap.Vertex)]             // Vertex2
        [InlineData(1, PickSnap.Edge)]                // Edge0To1
        [InlineData(7, PickSnap.Edge)]                // every tri edge
        [InlineData(65536, PickSnap.LineVertex)]
        [InlineData(262144, PickSnap.LineMiddle)]
        [InlineData(1048576, PickSnap.ArcCenter)]
        public void SnapFrom_ReadsTheResultBits(int bits, PickSnap expected)
        {
            Assert.Equal(expected, PickSnaps.From(bits));
        }

        [Fact]
        public void SnapFrom_PrefersTheTighterSnap_BecauseAPickReportsSeveralAtOnce()
        {
            // A vertex sits on two edges, so both bits come back; the vertex is
            // what the user aimed at.
            Assert.Equal(PickSnap.Vertex, PickSnaps.From(16 | 1 | 2));
            Assert.Equal(PickSnap.LineVertex, PickSnaps.From(65536 | 262144));
            Assert.Equal(PickSnap.ArcCenter, PickSnaps.From(1048576 | 262144));
        }

        [Theory]
        [InlineData(PickSnap.Vertex, true)]
        [InlineData(PickSnap.LineVertex, true)]
        [InlineData(PickSnap.LineMiddle, true)]
        [InlineData(PickSnap.ArcCenter, true)]
        [InlineData(PickSnap.Edge, false)]
        [InlineData(PickSnap.None, false)]
        public void IsPointSnap_SeparatesTheExactPointsFromTheRest(PickSnap snap, bool expected)
        {
            // This is what chooses between the two native measure cursors.
            Assert.Equal(expected, PickSnaps.IsPointSnap(snap));
        }

        [Theory]
        [InlineData(PickSnap.Vertex, "vertex")]
        [InlineData(PickSnap.Edge, "edge")]
        [InlineData(PickSnap.LineVertex, "line-vertex")]
        [InlineData(PickSnap.LineMiddle, "line-middle")]
        [InlineData(PickSnap.ArcCenter, "arc-center")]
        [InlineData(PickSnap.None, null)]
        public void SnapName_IsWhatScriptsSeeOnHitSnap(PickSnap snap, string expected)
        {
            Assert.Equal(expected, PickSnaps.Name(snap));
        }
    }
}
