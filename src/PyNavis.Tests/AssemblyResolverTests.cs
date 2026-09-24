using System;
using PyNavis;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Loader resolver behavior needed for multi-version builds: a runtime
    /// compiled against one Navisworks release's AdWindows.dll runs inside another
    /// release, so the exact strong-name version it requests is never on disk - the
    /// resolver must fall back to the same-named assembly the host already loaded.
    /// </summary>
    public class AssemblyResolverTests
    {
        private static System.Reflection.Assembly Resolve(string fullName) =>
            AssemblyResolver.ResolveForTest(new ResolveEventArgs(fullName));

        [Fact]
        public void MismatchedVersionRequest_FallsBackTo_AlreadyLoadedAssembly()
        {
            // xunit.core is definitely loaded in the test process; request an
            // impossible version of it, as Navisworks 2024 would for AdWindows 23.0.
            var loaded = typeof(FactAttribute).Assembly;
            var request = loaded.GetName().Name + ", Version=99.9.9.9, Culture=neutral";

            var resolved = Resolve(request);

            Assert.Same(loaded, resolved);
        }

        // AssemblyResolve is process-wide: every other add-in's failed bind lands in our
        // handler too. Answering those by simple name hands them OUR copy (or whatever
        // same-named assembly is loaded) at the wrong version, which is how pyRevit
        // broke Dynamo. A request from an assembly that is not ours is not our business.
        [Fact]
        public void RequestFromAForeignAssembly_IsNotAnswered()
        {
            var loaded = typeof(FactAttribute).Assembly;
            var foreignRequester = typeof(Assert).Assembly;   // xunit.assert: not pyNavis, not in the runtime dir
            var request = loaded.GetName().Name + ", Version=99.9.9.9, Culture=neutral";

            var resolved = AssemblyResolver.ResolveForTest(new ResolveEventArgs(request, foreignRequester));

            Assert.Null(resolved);
        }

        [Fact]
        public void RequestFromTheLoaderItself_IsAnswered()
        {
            var loaded = typeof(FactAttribute).Assembly;
            var request = loaded.GetName().Name + ", Version=99.9.9.9, Culture=neutral";

            var resolved = AssemblyResolver.ResolveForTest(
                new ResolveEventArgs(request, typeof(AssemblyResolver).Assembly));

            Assert.Same(loaded, resolved);
        }

        [Fact]
        public void IsOurs_TrueForAnAssemblyInsideTheRuntimeDir_FalseOutsideIt()
        {
            var assembly = typeof(FactAttribute).Assembly;
            var itsDir = System.IO.Path.GetDirectoryName(assembly.Location);

            Assert.True(AssemblyResolver.IsOurs(assembly, itsDir));
            Assert.True(AssemblyResolver.IsOurs(assembly, itsDir.ToUpperInvariant() + "\\"));
            Assert.False(AssemblyResolver.IsOurs(assembly, System.IO.Path.Combine(itsDir, "elsewhere")));
            Assert.False(AssemblyResolver.IsOurs(assembly, null));
        }

        // The generated pane satellite lives in its own Plugins folder and binds back to
        // PyNavis.dll through this handler, so the pyNavis name prefix counts as ours.
        [Fact]
        public void IsOurs_TrueForPyNavisNamedAssemblies_WhereverTheyLive()
        {
            Assert.True(AssemblyResolver.IsOurs(typeof(AssemblyResolver).Assembly, null));
        }

        [Fact]
        public void UnknownAssembly_StillResolvesToNull()
        {
            Assert.Null(Resolve("PyNavis.DoesNotExist, Version=1.0.0.0"));
        }

        [Fact]
        public void ResourceRequests_AreIgnored()
        {
            var name = typeof(FactAttribute).Assembly.GetName().Name;
            Assert.Null(Resolve(name + ".resources, Version=99.9.9.9"));
        }
    }
}
