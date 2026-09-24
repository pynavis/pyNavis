using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
// pythonnet's root "Python" namespace conflicts with IronPython's hosting class name.
using IronPythonHost = IronPython.Hosting.Python;

namespace PyNavis.Runtime.Engine
{
    /// <summary>
    /// IronPython 3.4 engine. One ScriptEngine per session (creation is expensive),
    /// a fresh ScriptScope per execution (predictable runs, edits picked up).
    /// The ONLY file in the runtime allowed to reference IronPython/DLR types.
    /// </summary>
    public class IronPythonEngine : IEngine
    {
        // Drops sys.modules entries whose source file lives under any of the given
        // roots, so bundle/lib module edits are picked up on the next run.
        // Stdlib and pynavislib modules stay cached.
        private const string InvalidatorCode = @"
import sys
for _name in list(sys.modules.keys()):
    _file = getattr(sys.modules[_name], '__file__', None)
    if _file and any(_file.lower().startswith(_p) for _p in __invalidate_paths__):
        del sys.modules[_name]
";

        private ScriptEngine _engine;
        private List<string> _basePaths;
        private CompiledCode _invalidator;

        public string Id => "ironpython";

        public void Initialize(EngineConfig config)
        {
            _engine = IronPythonHost.CreateEngine();
            _basePaths = _engine.GetSearchPaths()
                .Concat(config.IronPythonPaths)
                .Concat(config.SearchPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _engine.SetSearchPaths(_basePaths);
            Log.Info($"IronPython engine created: {_engine.LanguageVersion}; search paths: {string.Join("; ", _basePaths)}");
        }

        public ExecResult Execute(ScriptRequest request)
        {
            if (_engine == null) throw new InvalidOperationException("Engine not initialized.");

            var scope = _engine.CreateScope();
            // A hosted scope has no __name__, so the standard `if __name__ == '__main__':`
            // guard is False and the body of such a script silently never runs.
            scope.SetVariable("__name__", "__main__");
            foreach (var kv in request.Globals)
                scope.SetVariable(kv.Key, kv.Value);

            // Search paths and IO redirection are engine-global, shared by every run.
            // Set them for this request, then put the baseline back in the finally so a
            // nested run (a hook dispatched by a pump, a script calling Reload) cannot
            // leave the engine pointed at a dead writer or a foreign bundle folder.
            // Same discipline as CPythonEngine.Execute with sys.path/stdout/stderr.
            var io = _engine.Runtime.IO;

            try
            {
                _engine.SetSearchPaths(request.SearchPaths.Count == 0
                    ? _basePaths
                    : _basePaths.Concat(request.SearchPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

                if (request.SearchPaths.Count > 0)
                    InvalidateModulesUnder(request.SearchPaths);

                // sys.stdout writes to the STREAM half of SetOutput; a bare TextWriter never
                // sees script output, so wrap writers in a decoding stream (see TextWriterStream).
                io.RedirectToConsole();
                if (request.Output != null)
                    io.SetOutput(new Output.TextWriterStream(request.Output, Encoding.UTF8), Encoding.UTF8);
                if (request.ErrorOutput != null)
                    io.SetErrorOutput(new Output.TextWriterStream(request.ErrorOutput, Encoding.UTF8), Encoding.UTF8);

                var source = request.ScriptPath != null
                    ? _engine.CreateScriptSourceFromFile(request.ScriptPath)
                    : _engine.CreateScriptSourceFromString(request.Code, SourceCodeKind.Statements);
                source.Execute(scope);
                return new ExecResult { Succeeded = true };
            }
            catch (IronPython.Runtime.Exceptions.SystemExitException exit)
            {
                // sys.exit() is a script saying "stop here", not a crash: no traceback.
                // sys.exit('message') and a non-zero code are still failures, reported
                // in the script's own words.
                var code = exit.GetExitCode(out var other);
                if (other != null)
                    return new ExecResult { Succeeded = false, ErrorText = Convert.ToString(other) };
                return code == 0
                    ? new ExecResult { Succeeded = true }
                    : new ExecResult { Succeeded = false, ErrorText = "Script exited with code " + code + "." };
            }
            catch (Exception ex)
            {
                var eo = _engine.GetService<ExceptionOperations>();
                var errorText = eo != null ? eo.FormatException(ex) : ex.ToString();
                return new ExecResult { Succeeded = false, ErrorText = errorText };
            }
            finally
            {
                try
                {
                    _engine.SetSearchPaths(_basePaths);
                    io.RedirectToConsole();
                }
                catch (Exception ex)
                {
                    Log.Error("IronPython engine state could not be restored after a run.", ex);
                }
            }
        }

        public void InvalidateModulesUnder(IList<string> roots)
        {
            try
            {
                if (_invalidator == null)
                    _invalidator = _engine
                        .CreateScriptSourceFromString(InvalidatorCode, SourceCodeKind.Statements)
                        .Compile();

                // Trailing separator prevents prefix collisions (C:\a vs C:\ab).
                var normalized = roots
                    .Select(p => p.TrimEnd('\\').ToLowerInvariant() + "\\")
                    .ToList();
                var scope = _engine.CreateScope();
                scope.SetVariable("__invalidate_paths__", normalized);
                _invalidator.Execute(scope);
            }
            catch (Exception ex)
            {
                Log.Error("Module cache invalidation failed - stale modules may persist.", ex);
            }
        }
    }
}
