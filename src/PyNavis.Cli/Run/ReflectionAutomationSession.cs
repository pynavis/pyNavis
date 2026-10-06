using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace PyNavis.Cli
{
    /// <summary>
    /// A Navisworks started through the Autodesk Automation API. The API's assembly is
    /// loaded by reflection from the Navisworks folder, so the CLI itself references no
    /// Navisworks assembly and runs on any machine. Needs Navisworks, so no unit tests.
    /// </summary>
    public sealed class ReflectionAutomationSession : IAutomationSession
    {
        private const string AssemblyFile = "Autodesk.Navisworks.Automation.dll";
        private const string TypeName = "Autodesk.Navisworks.Api.Automation.NavisworksApplication";

        private readonly object _app;
        private readonly Type _type;

        /// <summary>Starts Navisworks from <paramref name="navisDir"/>, hidden. The window is
        /// shown later with <see cref="SetVisible"/>, after a model has opened: field-measured
        /// 2026-10-04 on Navisworks 2026, OpenFile into a window that is already visible ends
        /// the Navisworks process (RPC_S_CALL_FAILED), while opening hidden and showing
        /// afterwards works.</summary>
        public ReflectionAutomationSession(string navisDir)
        {
            _type = Assembly.LoadFrom(Path.Combine(navisDir, AssemblyFile)).GetType(TypeName, true);
            _app = Unwrap(() => Activator.CreateInstance(_type));
        }

        /// <inheritdoc />
        public void SetVisible(bool visible)
        {
            var property = _type.GetProperty("Visible") ?? throw new MissingMemberException(TypeName, "Visible");
            Unwrap(() => { property.SetValue(_app, visible); return null; });
        }

        /// <inheritdoc />
        public void OpenFile(string path) =>
            Call("OpenFile", new[] { typeof(string), typeof(string[]) }, path, new string[0]);

        /// <inheritdoc />
        public int ExecuteAddInPlugin(string pluginId, params string[] args) =>
            (int)Call("ExecuteAddInPlugin", new[] { typeof(string), typeof(string[]) }, pluginId, args);

        /// <inheritdoc />
        public void SaveFile(string path) =>
            Call("SaveFile", new[] { typeof(string) }, path);

        /// <summary>Closes the Navisworks this session started.</summary>
        public void Dispose() => Call("Dispose", Type.EmptyTypes);

        private object Call(string name, Type[] parameterTypes, params object[] args)
        {
            var method = _type.GetMethod(name, parameterTypes)
                ?? throw new MissingMethodException(TypeName, name);
            return Unwrap(() => method.Invoke(_app, args));
        }

        // Reflection wraps whatever the API threw; callers want the API's own exception.
        private static object Unwrap(Func<object> call)
        {
            try
            {
                return call();
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
