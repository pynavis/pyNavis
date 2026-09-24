using System;
using System.IO;
using System.Windows.Forms;

namespace PyNavis
{
    /// <summary>
    /// Minimal file logger for the loader. Deliberately dependency-free: if the runtime
    /// fails to load, this log is the only diagnostic left. Runtime has its own logger
    /// writing to the same folder.
    /// The LogPath getter is side-effect-free (no directory creation) so that error
    /// REPORTING can never itself throw - creation happens inside Write's try/catch.
    /// </summary>
    internal static class LoaderLog
    {
        private static readonly object Gate = new object();

        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyNavis", "logs");

        internal static string LogPath { get; } = Path.Combine(LogDir, "loader.log");

        internal static void Info(string message) => Write("INFO ", message);
        internal static void Error(string message) => Write("ERROR", message);

        /// <summary>Logs an exception and shows the standard pyNavis failure dialog. Never throws.</summary>
        internal static void ReportFatal(string context, Exception ex)
        {
            Error(context + " " + ex);
            try
            {
                MessageBox.Show(
                    context + "\n\n" + ex.Message + "\n\nDetails: " + LogPath,
                    "pyNavis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch
            {
                // Reporting must never take the host down.
            }
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDir);
                    File.AppendAllText(LogPath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging must never take the host down.
            }
        }
    }
}
