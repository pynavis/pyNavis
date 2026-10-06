using System;
using System.Collections.Generic;
using PyNavis.Runtime.Install;
using PyNavis.Runtime.Panes;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>The pure decisions behind the per-user install layout: which runtime
    /// folder the loader picks, which extension roots the host scans, and where the
    /// pane satellite is written for a bundled versus a Plugins-folder loader.</summary>
    public class PerUserLayoutTests
    {
        [Fact]
        public void Loader_Prefers_The_AppData_Runtime_Over_ProgramData()
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"C:\Users\u\AppData\Roaming\pyNavis\2026\runtime",
                @"C:\ProgramData\pyNavis\2026\runtime",
            };

            var picked = AssemblyResolver.FirstExistingRuntimeDir(
                @"C:\Users\u\AppData\Roaming", @"C:\ProgramData", "2026", existing.Contains, _ => true);

            Assert.Equal(@"C:\Users\u\AppData\Roaming\pyNavis\2026\runtime", picked);
        }

        [Fact]
        public void Loader_Falls_Back_To_ProgramData_Then_Null()
        {
            var onlyMachineWide = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"C:\ProgramData\pyNavis\2026\runtime",
            };
            Assert.Equal(@"C:\ProgramData\pyNavis\2026\runtime",
                AssemblyResolver.FirstExistingRuntimeDir(@"C:\Users\u\AppData\Roaming", @"C:\ProgramData", "2026", onlyMachineWide.Contains, _ => true));

            Assert.Null(AssemblyResolver.FirstExistingRuntimeDir(@"C:\Users\u\AppData\Roaming", @"C:\ProgramData", "2025", onlyMachineWide.Contains, _ => true));
            Assert.Null(AssemblyResolver.FirstExistingRuntimeDir(null, null, "2026", _ => true, _ => true));
        }

        /// <summary>Any user can create folders under C:\ProgramData, so a machine-wide
        /// runtime someone other than an administrator could have written is never loaded.</summary>
        [Fact]
        public void Loader_Skips_A_ProgramData_Runtime_That_Is_Not_Admin_Only()
        {
            var onlyMachineWide = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"C:\ProgramData\pyNavis\2026\runtime",
            };

            Assert.Null(AssemblyResolver.FirstExistingRuntimeDir(
                @"C:\Users\u\AppData\Roaming", @"C:\ProgramData", "2026", onlyMachineWide.Contains, _ => false));
        }

        [Fact]
        public void A_Machine_Wide_Extension_Root_Counts_Only_When_Admin_Only()
        {
            var machine = @"C:\ProgramData\pyNavis\extensions";
            var user = @"C:\Users\u\AppData\Roaming\pyNavis\extensions";

            Assert.True(InstallPaths.UsableDefaultRoot(user, machine, _ => true, _ => false));
            Assert.False(InstallPaths.UsableDefaultRoot(machine, machine, _ => true, _ => false));
            Assert.True(InstallPaths.UsableDefaultRoot(machine, machine, _ => true, _ => true));
            Assert.False(InstallPaths.UsableDefaultRoot(user, machine, _ => false, _ => true));
        }

        [Fact]
        public void Extension_Roots_Keep_Config_Order_Then_Add_Existing_Defaults_Once()
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"C:\Users\u\AppData\Roaming\pyNavis\extensions",
                @"C:\ProgramData\pyNavis\extensions",
            };
            var configured = new[] { @"D:\mine", @"c:\programdata\pynavis\extensions" };
            var defaults = new[] { @"C:\Users\u\AppData\Roaming\pyNavis\extensions", @"C:\ProgramData\pyNavis\extensions", @"C:\absent" };

            var roots = InstallPaths.ExtensionRoots(configured, defaults, existing.Contains);

            Assert.Equal(new[] { @"D:\mine", @"c:\programdata\pynavis\extensions", @"C:\Users\u\AppData\Roaming\pyNavis\extensions" }, roots);
        }

        [Fact]
        public void Satellite_Is_Written_Beside_A_Bundled_Loader_And_In_Plugins_Otherwise()
        {
            var navisDir = @"C:\Program Files\Autodesk\Navisworks Manage 2026";

            Assert.Equal(@"C:\Users\u\AppData\Roaming\Autodesk\ApplicationPlugins\pyNavis.bundle\Contents\2026",
                PaneSatellite.InstallDirectoryFor(
                    @"C:\Users\u\AppData\Roaming\Autodesk\ApplicationPlugins\pyNavis.bundle\Contents\2026\PyNavis.dll", navisDir));

            Assert.Equal(navisDir + @"\Plugins\PyNavisPanes",
                PaneSatellite.InstallDirectoryFor(navisDir + @"\Plugins\PyNavis\PyNavis.dll", navisDir));
        }
    }
}
