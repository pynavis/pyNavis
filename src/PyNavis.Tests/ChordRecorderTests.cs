using System;
using System.Threading;
using System.Windows.Input;
using PyNavis.Runtime.Forms;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Press-to-record chord capture: the pure key-to-chord mapping (validated
    /// through ChordParser so the recorder can never emit text the parser
    /// rejects) plus an STA construction smoke test of the control.
    /// </summary>
    public class ChordRecorderTests
    {
        [Fact]
        public void CtrlShiftM_MapsToCanonicalText()
        {
            var chord = ChordRecorder.ChordFromKey(
                Key.M, ModifierKeys.Control | ModifierKeys.Shift, false, out var error);

            Assert.Equal("Ctrl+Shift+M", chord);
            Assert.Null(error);
        }

        [Theory]
        [InlineData(Key.LeftCtrl)]
        [InlineData(Key.RightCtrl)]
        [InlineData(Key.LeftShift)]
        [InlineData(Key.LeftAlt)]
        [InlineData(Key.RightAlt)]
        public void ModifierAlone_IsStillRecording_NotAnError(Key key)
        {
            var chord = ChordRecorder.ChordFromKey(key, ModifierKeys.Control, false, out var error);

            Assert.Null(chord);
            Assert.Null(error);
        }

        [Fact]
        public void UnsupportedKey_ReturnsError()
        {
            var chord = ChordRecorder.ChordFromKey(
                Key.OemComma, ModifierKeys.Control, false, out var error);

            Assert.Null(chord);
            Assert.NotNull(error);
        }

        [Fact]
        public void BareKey_WithoutOptIn_ReturnsModifierError()
        {
            var chord = ChordRecorder.ChordFromKey(Key.M, ModifierKeys.None, false, out var error);

            Assert.Null(chord);
            Assert.Contains("Ctrl or Alt", error);
        }

        [Fact]
        public void BareKey_WithOptIn_Maps()
        {
            var chord = ChordRecorder.ChordFromKey(Key.F5, ModifierKeys.None, true, out var error);

            Assert.Equal("F5", chord);
            Assert.Null(error);
        }

        [Fact]
        public void ArrowChord_MapsByName()
        {
            var chord = ChordRecorder.ChordFromKey(
                Key.Left, ModifierKeys.Control | ModifierKeys.Shift, false, out var error);

            Assert.Equal("Ctrl+Shift+Left", chord);
            Assert.Null(error);
        }

        [Fact]
        public void Control_Builds_AndShowsPlaceholderWhenUnbound()
        {
            OnSta(() =>
            {
                var recorder = new ChordRecorder(allowBareKeys: false);
                Assert.Null(recorder.ChordText);
                Assert.Equal("press keys", recorder.DisplayText);

                recorder.ChordText = "Ctrl+Alt+K";
                Assert.Equal("Ctrl+Alt+K", recorder.DisplayText);
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
