using System;
using System.Collections.Generic;
using System.Linq;
using PyNavis.Runtime.Bundles;
using PyNavis.Runtime.Engine;

namespace PyNavis.Runtime.Events
{
    /// <summary>
    /// Runs hooks\&lt;event&gt;.py scripts on hub events. Synchronous on the UI thread
    /// (the API is not thread-safe); the hub already debounced the chatty events.
    /// A hook failing 3 times in a row is disabled until the next Reload.
    /// While a pyNavis command holds RunGate, every event except before-command and
    /// after-command skips its hooks rather than re-entering the shared engine.
    /// </summary>
    public static class HookRunner
    {
        public const int SoftBudgetMs = 500;

        private static readonly HookHealth Health = new HookHealth();
        private static List<HookModel> _hooks = new List<HookModel>();
        private static bool _subscribed;

        public static void Configure(IReadOnlyList<ExtensionModel> extensions)
        {
            _hooks = extensions.SelectMany(e => e.Hooks).ToList();
            Health.Reset();
            if (!_subscribed)
            {
                NavisEvents.Raised += OnEvent;
                _subscribed = true;
            }
            if (_hooks.Count > 0)
                Log.Info($"Hooks configured: {_hooks.Count} ({string.Join(", ", _hooks.Select(h => h.Id))}).");
        }

        private static void OnEvent(NavisEventArgs args)
        {
            // A running script pumps the message loop (UiPump), and a pump dispatches
            // the debounce timers and queued API events behind them - which would run
            // a hook's Python on top of the command's, sharing one engine. Refuse that.
            // before-command/after-command are exempt: ScriptExecutor raises those
            // deliberately, around the very run that holds the gate.
            var duringCommand = args.Event == NavisEvent.BeforeCommand
                             || args.Event == NavisEvent.AfterCommand;

            foreach (var hook in _hooks)
            {
                if (!string.Equals(hook.EventName, args.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (Health.IsDisabled(hook.Id)) continue;
                if (!duringCommand && Execution.RunGate.IsHeld)
                {
                    Log.Info($"Hook {hook.Id} skipped: '{Execution.RunGate.CurrentTitle}' is running.");
                    continue;
                }
                Run(hook, args);
            }
        }

        private static void Run(HookModel hook, NavisEventArgs args)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var source = "hook " + hook.Id;
            // A hook is not a command, so it never sets a command context; without this its
            // log lines would be labelled with whichever button the user last clicked.
            Execution.PyNavisHost.Instance.LogSource = source;
            try
            {
                var writer = new Output.LogWriter(source);
                var request = new ScriptRequest
                {
                    ScriptPath = hook.ScriptPath,
                    Output = writer,
                    ErrorOutput = writer,
                };
                foreach (var path in hook.SearchPaths) request.SearchPaths.Add(path);
                request.Globals["__file__"] = hook.ScriptPath;
                request.Globals["__event__"] = new Dictionary<string, object>
                {
                    ["name"] = args.Name,
                    ["command"] = args.CommandKey,
                    ["viewpoint"] = args.ViewpointName,
                };
                request.Globals["__pynavis__"] = Execution.PyNavisHost.Instance;

                var result = EngineManager.GetEngine(hook.EngineId).Execute(request);
                if (result.Succeeded) Health.RecordSuccess(hook.Id);
                else Fail(hook, result.ErrorText);
            }
            catch (Exception ex)
            {
                Fail(hook, ex.Message);
            }
            finally
            {
                // Cleared rather than left set: between hooks nothing pyNavis-owned is
                // running, and a stale label is what caused this bug in the first place.
                Execution.PyNavisHost.Instance.LogSource = null;
                watch.Stop();
                if (watch.ElapsedMilliseconds > SoftBudgetMs)
                    Log.Info($"Hook {hook.Id} took {watch.ElapsedMilliseconds}ms " +
                             $"(soft budget {SoftBudgetMs}ms) - keep hooks fast.");
            }
        }

        private static void Fail(HookModel hook, string error)
        {
            Log.Error($"Hook {hook.Id} failed:\n{error}");
            if (Health.RecordFailure(hook.Id))
                Forms.Toast.Show("warning", $"Hook {hook.Id} disabled after 3 failures",
                                 "Fix the script, then Reload pyNavis to re-enable it.");
        }
    }
}
