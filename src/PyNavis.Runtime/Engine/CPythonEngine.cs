using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Python.Runtime;

namespace PyNavis.Runtime.Engine
{
    /// <summary>
    /// CPython engine hosted via pythonnet. ONE interpreter per process (CPython
    /// cannot be cleanly restarted in-proc), initialized lazily and never shut down -
    /// the host process exit tears it down. Fresh scope per execution, sys.path and
    /// sys.stdout/stderr reset around every run so requests never leak into each other.
    /// The ONLY file in the runtime allowed to reference Python.Runtime types.
    /// </summary>
    public class CPythonEngine : IEngine
    {
        private const string InvalidatorCode = @"
import sys
for _name in list(sys.modules.keys()):
    _file = getattr(sys.modules[_name], '__file__', None)
    if _file and any(_file.lower().startswith(_p) for _p in __invalidate_paths__):
        del sys.modules[_name]
";

        private static readonly object InitLock = new object();

        // sys.path/sys.stdout/sys.stderr are interpreter-global and the GIL cycles
        // during exec, so concurrent Executes would interleave on them. One run at a
        // time, process-wide. (C# locks are reentrant: a script triggering Reload ->
        // InvalidateModulesUnder on the same thread does not deadlock.)
        private static readonly object ExecLock = new object();

        private static bool _pythonInitialized;

        private List<string> _basePaths;
        private List<string> _sharedPaths;

        public string Id => "cpython";

        public void Initialize(EngineConfig config)
        {
            lock (InitLock)
            {
                if (!_pythonInitialized)
                {
                    var dll = CPythonLocator.Resolve(config.CPythonDll);
                    if (dll == null)
                        throw new InvalidOperationException(
                            "No CPython found. Install Python 3 (python.org) or set the \"cpython\" "
                            + "key in %APPDATA%\\pyNavis\\config.json to a python3XX.dll or install dir.");

                    Python.Runtime.Runtime.PythonDLL = dll;
                    PythonEngine.Initialize();
                    // Release the GIL taken by Initialize so any later thread can acquire it.
                    PythonEngine.BeginAllowThreads();
                    _pythonInitialized = true;
                    Log.Info($"CPython engine created: {PythonEngine.Version.Trim()} ({dll})");
                }
            }

            lock (ExecLock)
            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                var interpreterPaths = new List<string>();
                foreach (var p in sys.path)
                    interpreterPaths.Add((string)p);
                // config.IronPythonPaths deliberately excluded: that stdlib is IronPython's.
                _basePaths = interpreterPaths;
                _sharedPaths = config.SearchPaths.ToList();
            }
        }

        public ExecResult Execute(ScriptRequest request)
        {
            if (_basePaths == null) throw new InvalidOperationException("Engine not initialized.");

            lock (ExecLock)
            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                PyObject oldStdout = sys.stdout;
                PyObject oldStderr = sys.stderr;
                PyObject oldPath = sys.path;

                try
                {
                    var paths = _sharedPaths
                        .Concat(request.SearchPaths)
                        .Concat(_basePaths)
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                    var pyPath = new PyList();
                    foreach (var p in paths) pyPath.Append(new PyString(p));
                    sys.path = pyPath;

                    if (request.SearchPaths.Count > 0)
                        InvalidateModulesUnderLocked(request.SearchPaths);

                    if (request.Output != null) sys.stdout = new PyWriter(request.Output).ToPython();
                    if (request.ErrorOutput != null) sys.stderr = new PyWriter(request.ErrorOutput).ToPython();

                    using (var scope = Py.CreateScope())
                    {
                        // Same as IronPythonEngine: without it `if __name__ == '__main__':`
                        // is False and the body of such a script silently never runs.
                        scope.Set("__name__", new PyString("__main__"));
                        foreach (var kv in request.Globals)
                            scope.Set(kv.Key, kv.Value.ToPython());

                        var code = request.ScriptPath != null
                            ? TextFiles.Read(request.ScriptPath)
                            : request.Code;
                        scope.Set("__pynavis_source__", new PyString(code));
                        scope.Set("__pynavis_file__", new PyString(request.ScriptPath ?? "<script>"));
                        if (request.ScriptPath != null && !request.Globals.ContainsKey("__file__"))
                            scope.Set("__file__", new PyString(request.ScriptPath));

                        scope.Exec("exec(compile(__pynavis_source__, __pynavis_file__, 'exec'))");
                    }
                    return new ExecResult { Succeeded = true };
                }
                catch (PythonException pex) when (pex.Type != null && pex.Type.Name == "SystemExit")
                {
                    // sys.exit() is a script saying "stop here", not a crash: no traceback.
                    // A message or a non-zero code is still a failure, in the script's own
                    // words. Mirrors IronPythonEngine so both engines end a run alike.
                    return ExitResult(pex);
                }
                catch (PythonException pex)
                {
                    return new ExecResult { Succeeded = false, ErrorText = pex.Format() };
                }
                catch (Exception ex)
                {
                    return new ExecResult { Succeeded = false, ErrorText = ex.ToString() };
                }
                finally
                {
                    sys.stdout = oldStdout;
                    sys.stderr = oldStderr;
                    sys.path = oldPath;
                }
            }
        }

        // Caller holds the GIL.
        private static ExecResult ExitResult(PythonException exit)
        {
            try
            {
                using (var code = exit.Value?.GetAttr("code"))
                {
                    if (code == null || code.IsNone()) return new ExecResult { Succeeded = true };
                    if (PyInt.IsIntType(code))
                    {
                        var number = code.As<int>();
                        return number == 0
                            ? new ExecResult { Succeeded = true }
                            : new ExecResult { Succeeded = false, ErrorText = "Script exited with code " + number + "." };
                    }
                    return new ExecResult { Succeeded = false, ErrorText = code.ToString() };
                }
            }
            catch (Exception ex)
            {
                return new ExecResult { Succeeded = false, ErrorText = "Script exited: " + ex.Message };
            }
        }

        public void InvalidateModulesUnder(IList<string> roots)
        {
            if (!_pythonInitialized) return;
            lock (ExecLock)
            using (Py.GIL())
                InvalidateModulesUnderLocked(roots);
        }

        // Caller must hold the GIL.
        private void InvalidateModulesUnderLocked(IList<string> roots)
        {
            try
            {
                using (var scope = Py.CreateScope())
                {
                    var normalized = new PyList();
                    foreach (var r in roots)
                        normalized.Append(new PyString(r.TrimEnd('\\').ToLowerInvariant() + "\\"));
                    scope.Set("__invalidate_paths__", normalized);
                    scope.Exec(InvalidatorCode);
                }
            }
            catch (Exception ex)
            {
                Log.Error("CPython module cache invalidation failed - stale modules may persist.", ex);
            }
        }

        /// <summary>python file-like shim: routes sys.stdout/stderr into a TextWriter.</summary>
        public sealed class PyWriter
        {
            private readonly TextWriter _writer;

            public PyWriter(TextWriter writer) { _writer = writer; }

            public void write(string text) => _writer.Write(text);
            public void flush() => _writer.Flush();
        }
    }
}
