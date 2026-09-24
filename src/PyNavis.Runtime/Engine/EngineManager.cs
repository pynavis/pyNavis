using System;
using System.Collections.Generic;

namespace PyNavis.Runtime.Engine
{
    /// <summary>
    /// Holds one initialized engine instance per engine id, created lazily on first
    /// request (bundle.yaml "engine:" selects per bundle; ironpython is the default,
    /// cpython initializes the machine's CPython on first use).
    /// </summary>
    public static class EngineManager
    {
        private static readonly Dictionary<string, IEngine> Engines =
            new Dictionary<string, IEngine>(StringComparer.OrdinalIgnoreCase);

        private static EngineConfig _config = new EngineConfig();

        public const string DefaultEngineId = "ironpython";

        /// <summary>
        /// Sets the config baked into engines at creation. Call before the first
        /// GetEngine (RuntimeHost.Boot does); engines already created keep their config.
        /// </summary>
        public static void Configure(EngineConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (Engines.Count > 0)
                Log.Error("EngineManager.Configure called after engines were created - existing engines keep their old config.");
            _config = config;
        }

        /// <summary>Drops cached modules under the given roots on every created engine.</summary>
        public static void InvalidateModules(IList<string> roots)
        {
            foreach (var engine in Engines.Values)
                engine.InvalidateModulesUnder(roots);
        }

        public static IEngine GetEngine(string id = DefaultEngineId)
        {
            if (Engines.TryGetValue(id, out var engine)) return engine;

            switch (id.ToLowerInvariant())
            {
                case "ironpython":
                    engine = new IronPythonEngine();
                    break;
                case "cpython":
                    engine = new CPythonEngine();
                    break;
                default:
                    throw new NotSupportedException($"Unknown engine '{id}'.");
            }

            engine.Initialize(_config);
            Engines[id] = engine;
            return engine;
        }
    }
}
