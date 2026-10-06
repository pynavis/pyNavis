using System;

namespace PyNavis.Cli
{
    /// <summary>
    /// One Navisworks instance driven from outside, the three calls <c>pynavis run</c>
    /// needs. The real one is <see cref="ReflectionAutomationSession"/>; tests use a fake.
    /// Disposing closes that Navisworks, and may come from another thread while a call
    /// is still blocked (a timeout, or Ctrl+C).
    /// </summary>
    public interface IAutomationSession : IDisposable
    {
        /// <summary>Opens a model, replacing the one open; throws when it cannot.</summary>
        void OpenFile(string path);

        /// <summary>Runs an add-in plugin by its id and returns what its Execute returned.</summary>
        int ExecuteAddInPlugin(string pluginId, params string[] args);

        /// <summary>Saves the open model to <paramref name="path"/>.</summary>
        void SaveFile(string path);

        /// <summary>Shows or hides the Navisworks window. A session starts hidden.</summary>
        void SetVisible(bool visible);
    }
}
