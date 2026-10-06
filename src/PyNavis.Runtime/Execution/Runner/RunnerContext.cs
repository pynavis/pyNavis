using System;
using System.IO;

namespace PyNavis.Runtime.Execution.Runner
{
    /// <summary>
    /// Marks that a <c>pynavis run</c> script is executing and holds the transcript its
    /// output goes to. While active, nothing may open a window, because no one is there
    /// to close it: toasts, alerts and the output window write here instead, and dialogs
    /// that need an answer throw.
    /// </summary>
    /// <remarks>
    /// Not thread-safe by design: the Navisworks API is single-threaded and so is the
    /// runner, which enters one run at a time.
    /// </remarks>
    public static class RunnerContext
    {
        /// <summary>True while a run executes.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>Where the run's output goes; null when inactive.</summary>
        public static TextWriter Transcript { get; private set; }

        /// <summary>Starts a run writing to <paramref name="transcript"/>; disposing the
        /// result ends it. Runs do not nest: entering while one is active throws, since
        /// ending the inner run would otherwise end the outer one too.</summary>
        public static IDisposable Enter(TextWriter transcript)
        {
            if (transcript == null) throw new ArgumentNullException(nameof(transcript));
            if (IsActive) throw new InvalidOperationException("A pynavis run is already active.");
            Transcript = transcript;
            IsActive = true;
            return new Scope();
        }

        /// <summary>Writes <paramref name="line"/> and a newline to the transcript while a
        /// run is active; does nothing otherwise. Never throws: toasts and alerts write
        /// through here, and neither is worth failing a script over, so a transcript that
        /// cannot be written (disposed, disk full) is logged instead.</summary>
        public static void Note(string line)
        {
            if (!IsActive) return;
            try
            {
                Transcript.WriteLine(line);
            }
            catch (Exception ex)
            {
                Log.Error("Could not write to the run transcript", ex);
            }
        }

        /// <summary>
        /// Throws when a run is active. Every script-facing dialog that waits for an answer
        /// calls this first: under pynavis run no one is there to give one, so the dialog
        /// would hang the hidden Navisworks forever.
        /// </summary>
        public static void ThrowIfActive()
        {
            if (IsActive)
                throw new InvalidOperationException(
                    "Dialogs are not available under pynavis run. Check __runner__ and skip the prompt.");
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose()
            {
                IsActive = false;
                Transcript = null;
            }
        }
    }
}
