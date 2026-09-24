using System.Collections.Generic;
using System.IO;

namespace PyNavis.Runtime.Engine
{
    /// <summary>
    /// The engine seam. IronPython today, CPython (pythonnet) later. Nothing outside
    /// the concrete engine classes may reference IronPython types - that rule is what
    /// keeps the CPython door open.
    /// </summary>
    public interface IEngine
    {
        /// <summary>"ironpython" or (later) "cpython".</summary>
        string Id { get; }

        void Initialize(EngineConfig config);

        ExecResult Execute(ScriptRequest request);

        /// <summary>
        /// Drops cached modules whose source lives under any of the given directories,
        /// so the next import re-reads them (used by Reload for pynavislib edits).
        /// </summary>
        void InvalidateModulesUnder(IList<string> roots);
    }

    public class EngineConfig
    {
        /// <summary>Directories every engine adds to script module search paths (pynavislib, ...).</summary>
        public IList<string> SearchPaths { get; } = new List<string>();

        /// <summary>
        /// Directories only the IronPython engine may add (the bundled 3.4-era stdlib).
        /// CPython ships its own stdlib and must never see these.
        /// </summary>
        public IList<string> IronPythonPaths { get; } = new List<string>();

        /// <summary>Explicit CPython dll or install dir from config ("cpython" key); null = auto-detect.</summary>
        public string CPythonDll { get; set; }
    }

    public class ScriptRequest
    {
        /// <summary>Path to script.py; null when executing <see cref="Code"/> directly.</summary>
        public string ScriptPath { get; set; }

        /// <summary>Inline code to execute; used when <see cref="ScriptPath"/> is null.</summary>
        public string Code { get; set; }

        /// <summary>Extra module search paths for this run (bundle dir, extension lib).</summary>
        public IList<string> SearchPaths { get; } = new List<string>();

        /// <summary>Variables injected into the script scope (__window__, __file__, ...).</summary>
        public IDictionary<string, object> Globals { get; } = new Dictionary<string, object>();

        public TextWriter Output { get; set; }
        public TextWriter ErrorOutput { get; set; }
    }

    public class ExecResult
    {
        public bool Succeeded { get; set; }
        /// <summary>Engine-formatted traceback / error text when <see cref="Succeeded"/> is false.</summary>
        public string ErrorText { get; set; }
    }
}
