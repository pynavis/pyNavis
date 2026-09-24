using System;
using System.IO;

namespace PyNavis.Runtime
{
    /// <summary>
    /// Runtime file logger: %APPDATA%\pyNavis\logs\pyNavis.log.
    /// There is no debugger story inside Navisworks - this log IS the debugger.
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static string LogDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyNavis", "logs");

        private static string LogPath
        {
            get
            {
                if (_path == null)
                {
                    Directory.CreateDirectory(LogDir);
                    _path = Path.Combine(LogDir, "pyNavis.log");
                }
                return _path;
            }
        }

        public static void Info(string message) => Write("INFO ", message);
        public static void Error(string message) => Write("ERROR", message);
        public static void Error(string message, Exception ex) => Write("ERROR", message + Environment.NewLine + ex);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                    File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never take the host down.
            }
        }
    }
}
