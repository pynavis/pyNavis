using System.Text;

namespace PyNavis.Runtime.Output
{
    /// <summary>
    /// TextWriter that lands lines in the pyNavis log. Hooks, startup.py, and
    /// smartbutton init write here instead of opening an output window.
    /// </summary>
    public class LogWriter : System.IO.TextWriter
    {
        private readonly string _prefix;
        private readonly StringBuilder _line = new StringBuilder();

        public LogWriter(string prefix) { _prefix = prefix; }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n') { Flush(); return; }
            if (value != '\r') _line.Append(value);
        }

        public override void Flush()
        {
            if (_line.Length == 0) return;
            Log.Info($"[{_prefix}] {_line}");
            _line.Clear();
        }
    }
}
