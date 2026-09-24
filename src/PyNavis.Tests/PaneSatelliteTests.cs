using System;
using System.IO;
using PyNavis.Runtime.Panes;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The pure decision logic behind Finding 1's fix: whether the satellite is already
    /// loaded (so any write is doomed until a restart), and which of the two honest
    /// messages a write failure gets. Neither needs a Navisworks install or a write under
    /// Program Files - that is the point of splitting them out of Generate.
    /// </summary>
    public class PaneSatelliteTests
    {
        [Fact]
        public void IsAssemblyLoaded_True_When_PyNavisPanes_Is_Among_The_Loaded_Names()
        {
            Assert.True(PaneSatellite.IsAssemblyLoaded(new[] { "mscorlib", "PyNavisPanes", "PyNavis" }));
        }

        [Fact]
        public void IsAssemblyLoaded_Is_Case_Insensitive()
        {
            Assert.True(PaneSatellite.IsAssemblyLoaded(new[] { "pynavispanes" }));
        }

        [Fact]
        public void IsAssemblyLoaded_False_When_Not_Present()
        {
            Assert.False(PaneSatellite.IsAssemblyLoaded(new[] { "mscorlib", "PyNavis" }));
        }

        [Fact]
        public void IsAssemblyLoaded_False_For_Null_Or_Empty()
        {
            Assert.False(PaneSatellite.IsAssemblyLoaded(null));
            Assert.False(PaneSatellite.IsAssemblyLoaded(new string[0]));
        }

        [Fact]
        public void DescribeFailure_For_An_Access_Denial_Says_Administrator()
        {
            var ex = PaneSatellite.DescribeFailure(@"C:\Program Files\Navisworks\Plugins\PyNavisPanes",
                new UnauthorizedAccessException("denied"));

            Assert.Contains("administrator", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("in use", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DescribeFailure_For_An_Access_Denial_In_The_Bundle_Does_Not_Say_Administrator()
        {
            var ex = PaneSatellite.DescribeFailure(@"C:\Users\u\AppData\Roaming\Autodesk\ApplicationPlugins\pyNavis.bundle\Contents\2026",
                new UnauthorizedAccessException("denied"), bundled: true);

            Assert.DoesNotContain("administrator", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("profile", ex.Message);
        }

        [Fact]
        public void DescribeFailure_For_An_IOException_Says_Restart_Not_Administrator()
        {
            var ex = PaneSatellite.DescribeFailure(@"C:\Program Files\Navisworks\Plugins\PyNavisPanes",
                new IOException("The process cannot access the file because it is being used by another process."));

            Assert.DoesNotContain("administrator", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("restart", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DescribeFailure_Without_An_Exception_Says_Restart_Not_Administrator()
        {
            // No caller passes null since the up-front "already loaded" refusal was
            // removed, but the null branch stays defensive: it must never be the branch
            // that tells someone to relaunch as administrator, which fixes nothing unless
            // the failure was an actual access denial.
            var ex = PaneSatellite.DescribeFailure(@"C:\Program Files\Navisworks\Plugins\PyNavisPanes", null);

            Assert.DoesNotContain("administrator", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("restart", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
