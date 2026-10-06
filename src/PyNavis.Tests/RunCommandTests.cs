using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The CLI drives a hidden Navisworks by plugin id, so the add-in's registration is a
    /// contract: id PyNavis.Run, developer id PYNV, and no ribbon button. Read from the
    /// built loader in the reflection-only context, where the Navisworks API it derives
    /// from can be resolved for metadata without loading it into the test process.
    /// </summary>
    public class RunCommandTests
    {
        private static string NavisApiDir =>
            typeof(RunCommandTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "PyNavis.Tests.NavisApiDir").Value;

        private static Type ReflectionOnlyRunCommand()
        {
            var binDir = AppDomain.CurrentDomain.BaseDirectory;
            ResolveEventHandler resolve = (sender, e) =>
            {
                var file = new AssemblyName(e.Name).Name + ".dll";
                foreach (var dir in new[] { NavisApiDir, binDir })
                {
                    var candidate = Path.Combine(dir, file);
                    if (File.Exists(candidate)) return Assembly.ReflectionOnlyLoadFrom(candidate);
                }
                return Assembly.ReflectionOnlyLoad(e.Name);
            };
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += resolve;
            try
            {
                var loader = Assembly.ReflectionOnlyLoadFrom(Path.Combine(binDir, "PyNavis.dll"));
                var type = loader.GetType("PyNavis.RunCommand", throwOnError: true);
                // Touch the attributes and base type while the resolver is still attached.
                CustomAttributeData.GetCustomAttributes(type);
                Assert.Equal("Autodesk.Navisworks.Api.Plugins.AddInPlugin", type.BaseType.FullName);
                return type;
            }
            finally
            {
                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= resolve;
            }
        }

        [Fact]
        public void RunCommand_IsRegisteredAsPyNavisRun_WithNoRibbonButton()
        {
            var type = ReflectionOnlyRunCommand();
            var attributes = CustomAttributeData.GetCustomAttributes(type);

            var plugin = attributes.Single(a => a.AttributeType.FullName == "Autodesk.Navisworks.Api.Plugins.PluginAttribute");
            Assert.Equal("PyNavis.Run", plugin.ConstructorArguments[0].Value);
            Assert.Equal("PYNV", plugin.ConstructorArguments[1].Value);
            Assert.Equal("pyNavis Run",
                plugin.NamedArguments.Single(n => n.MemberName == "DisplayName").TypedValue.Value);

            var addIn = attributes.Single(a => a.AttributeType.FullName == "Autodesk.Navisworks.Api.Plugins.AddInPluginAttribute");
            var location = addIn.ConstructorArguments[0];
            Assert.Equal("Autodesk.Navisworks.Api.Plugins.AddInLocation", location.ArgumentType.FullName);
            Assert.Equal(location.ArgumentType.GetField("None").GetRawConstantValue(), location.Value);
        }
    }
}
