using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CSharp;
using PyNavis.Runtime.Config;
using PyNavis.Runtime.Install;

namespace PyNavis.Runtime.Panes
{
    /// <summary>
    /// Generates and installs PyNavisPanes.dll, which carries pane slots beyond the five
    /// compiled into the loader. Navisworks only scans for plugins at startup, so a
    /// generated satellite takes effect after a restart.
    /// </summary>
    public static class PaneSatellite
    {
        private const string AssemblyName = "PyNavisPanes";

        /// <summary>The folder the satellite is written to. Inside the per-user bundle
        /// that is the loader's own Contents\{year} folder, and the manifest gets a
        /// ComponentEntry for it. Outside a bundle (a developer's Plugins deploy) it is
        /// a sibling plugin folder, which must be named exactly like the dll inside it.</summary>
        public static string InstallDirectory => InstallDirectoryFor(LoaderAssemblyPath(), NavisDir);

        /// <summary>Pure half of <see cref="InstallDirectory"/>.</summary>
        internal static string InstallDirectoryFor(string loaderPath, string navisDir)
        {
            var bundle = BundleLayout.FromLoaderPath(loaderPath);
            return bundle != null ? bundle.ContentsDir : Path.Combine(navisDir, "Plugins", AssemblyName);
        }

        private static string NavisDir => Path.GetDirectoryName(
            typeof(Autodesk.Navisworks.Api.Application).Assembly.Location);

        /// <summary>Generates slots 6..(5+extraSlots) and returns the resulting total slot
        /// count. Throws with a user-readable message when it cannot write.</summary>
        public static int Generate(int extraSlots)
        {
            if (extraSlots < 0) extraSlots = 0;
            var total = PaneRegistry.ShippedSlots + extraSlots;

            var bundle = BundleLayout.FromLoaderPath(LoaderAssemblyPath());
            var dir = InstallDirectoryFor(LoaderAssemblyPath(), NavisDir);
            var target = Path.Combine(dir, AssemblyName + ".dll");

            // Navisworks loads every plugin DLL at startup and keeps the file mapped, so
            // once a WORKING satellite exists it is locked for the whole session, from
            // process start, whether or not a slot 6+ was ever opened.
            //
            // This must be detected up front rather than left to fail, because the failure
            // it produces is actively misleading: deleting or overwriting a mapped image
            // returns ERROR_ACCESS_DENIED, which surfaces as UnauthorizedAccessException
            // and is indistinguishable from a real permissions problem. Field-measured:
            // that sent the user a "restart as administrator" toast for a file
            // whose ACL already granted Modify to Authenticated Users; elevation could not
            // have helped.
            //
            // (An earlier pass here removed this check after measuring the file as
            // writable. That measurement was taken against a satellite whose assembly name
            // did not match its file name, so Navisworks had ignored and never loaded it.
            // Do not re-test this on a satellite that is not actually in use.)
            if (File.Exists(target) && IsSatelliteAssemblyLoaded())
                throw new InvalidOperationException(
                    "Navisworks has already loaded the current panel slots, so the count cannot " +
                    "be changed now. Close Navisworks, delete " + (bundle != null ? target : dir) +
                    ", then start it again and set the count.");

            if (extraSlots == 0)
            {
                if (File.Exists(target))
                {
                    try
                    {
                        File.Delete(target);
                    }
                    catch (Exception ex)
                    {
                        throw DescribeFailure(dir, ex, bundle != null);
                    }
                }
                if (bundle != null) RegisterInManifest(bundle, false);
                else RemoveIfEmpty(dir);
                Save(0);
                return PaneRegistry.ShippedSlots;
            }

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                throw DescribeFailure(dir, ex, bundle != null);
            }

            var source = PaneSatelliteSource.Emit(PaneRegistry.ShippedSlots + 1, total);

            // Compile beside the target rather than straight over it: the target may be
            // locked, and a failed in-place compile can leave a half-written dll. The temp
            // copy only replaces the target once it exists intact.
            //
            // It goes in a temp DIRECTORY, keeping the file name exactly PyNavisPanes.dll,
            // because CodeDom takes the assembly's identity from the output file name.
            // Compiling to "PyNavisPanes.<guid>.tmp" and renaming the file afterwards left
            // the assembly still named PyNavisPanes.<guid> inside, and Navisworks then
            // ignored it: field-measured, the extra panels silently never
            // appeared and only the five shipped slots were listed.
            var tempDir = Path.Combine(dir, "." + AssemblyName + "." + Guid.NewGuid().ToString("N"));
            var tempTarget = Path.Combine(tempDir, AssemblyName + ".dll");
            try
            {
                Directory.CreateDirectory(tempDir);
            }
            catch (Exception ex)
            {
                throw DescribeFailure(dir, ex, bundle != null);
            }
            var parameters = new CompilerParameters
            {
                GenerateExecutable = false,
                OutputAssembly = tempTarget,
                CompilerOptions = "/optimize",
            };
            parameters.ReferencedAssemblies.Add("mscorlib.dll");
            parameters.ReferencedAssemblies.Add("System.dll");
            parameters.ReferencedAssemblies.Add("System.Windows.Forms.dll");
            parameters.ReferencedAssemblies.Add(
                typeof(Autodesk.Navisworks.Api.Application).Assembly.Location);
            parameters.ReferencedAssemblies.Add(LoaderAssemblyPath());

            try
            {
                using (var provider = new CSharpCodeProvider())
                {
                    var results = provider.CompileAssemblyFromSource(parameters, source);
                    if (results.Errors.HasErrors)
                    {
                        var first = results.Errors.Cast<CompilerError>().First(e => !e.IsWarning);
                        Log.Error("Pane satellite failed to compile: " + first.ErrorText);
                        TryDeleteScratch(tempDir);
                        throw new InvalidOperationException(
                            "Could not generate the extra panel slots: " + first.ErrorText);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDeleteScratch(tempDir);
                throw DescribeFailure(dir, ex, bundle != null);
            }
            catch (IOException ex)
            {
                TryDeleteScratch(tempDir);
                throw DescribeFailure(dir, ex, bundle != null);
            }

            try
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(tempTarget, target);
                TryDeleteScratch(tempDir);
            }
            catch (Exception ex)
            {
                TryDeleteScratch(tempDir);
                throw DescribeFailure(dir, ex, bundle != null);
            }

            if (bundle != null) RegisterInManifest(bundle, true);
            Save(extraSlots);
            Log.Info("Pane satellite written to " + target + " (" + total + " slots after restart).");
            return total;
        }

        /// <summary>Boot-time self-heal for a bundled install: a manifest entry for a
        /// PyNavisPanes.dll that no longer exists (the user deleted it to reset the count)
        /// is dropped, so Navisworks never chases a missing component. Never throws.</summary>
        public static void ReconcileManifest()
        {
            try
            {
                var bundle = BundleLayout.FromLoaderPath(LoaderAssemblyPath());
                if (bundle == null || !File.Exists(bundle.ManifestPath)) return;
                if (File.Exists(Path.Combine(bundle.ContentsDir, AssemblyName + ".dll"))) return;
                RegisterInManifest(bundle, false);
            }
            catch (Exception ex)
            {
                Log.Error("Could not reconcile the bundle manifest: " + ex.Message);
            }
        }

        /// <summary>Adds or removes the satellite's ComponentEntry in the bundle manifest,
        /// without which Navisworks never loads a dll that merely sits in Contents.</summary>
        private static void RegisterInManifest(BundleLayout bundle, bool present)
        {
            if (!File.Exists(bundle.ManifestPath))
            {
                if (!present) return;
                throw new InvalidOperationException(
                    "The pyNavis bundle manifest is missing: " + bundle.ManifestPath +
                    ". Reinstall pyNavis, then set the slot count again.");
            }
            var doc = PackageManifest.Load(bundle.ManifestPath);
            var version = (string)doc.Root.Attribute("AppVersion") ?? "0.0.0";
            if (present)
                PackageManifest.Register(doc, bundle.Year, AssemblyName + ".dll", "pyNavis panel slots", version);
            else if (!PackageManifest.Unregister(doc, bundle.Year, AssemblyName + ".dll"))
                return;
            PackageManifest.Save(doc, bundle.ManifestPath);
        }

        /// <summary>Pure: whether AssemblyName appears among the given loaded assembly
        /// names. Split from <see cref="IsSatelliteAssemblyLoaded"/> so the decision is
        /// unit-testable without a real AppDomain full of Navisworks assemblies.</summary>
        internal static bool IsAssemblyLoaded(IEnumerable<string> loadedAssemblyNames)
        {
            if (loadedAssemblyNames == null) return false;
            foreach (var name in loadedAssemblyNames)
                if (string.Equals(name, AssemblyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>True once PyNavisPanes.dll has been loaded into this process, which for
        /// a satellite Navisworks recognises means from startup onwards.</summary>
        private static bool IsSatelliteAssemblyLoaded()
        {
            var names = new List<string>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { names.Add(assembly.GetName().Name); }
                catch { /* a dynamic or reflection-only assembly; not our satellite */ }
            }
            return IsAssemblyLoaded(names);
        }

        /// <summary>
        /// The clear, actionable message for a write failure under the Navisworks install
        /// folder, instead of a raw exception reaching the bundle script. Pure: takes the
        /// failing exception and picks between the two honest outcomes. A genuine
        /// access-control denial
        /// (<see cref="UnauthorizedAccessException"/>) is the only case elevation can fix;
        /// everything else - the satellite already loaded, or its file otherwise locked -
        /// can only be fixed by restarting Navisworks, so it must not say "administrator".
        /// </summary>
        internal static InvalidOperationException DescribeFailure(string dir, Exception ex, bool bundled = false)
        {
            if (ex is UnauthorizedAccessException)
                return new InvalidOperationException(
                    bundled
                        ? "Cannot write to " + dir + ". The folder is in your own profile, so check that " +
                          "a security tool or sync client is not blocking it, then try again."
                        : "Cannot write to " + dir + ". Restart Navisworks as administrator and try again.", ex);

            return new InvalidOperationException(
                "The current panel slots are in use, so Navisworks must be restarted before the " +
                "slot count can change.", ex);
        }

        /// <summary>Deletes without throwing: cleanup of the scratch folder the satellite
        /// is compiled in is best-effort, and must never mask the real failure with a new
        /// one, nor turn a successful write into a reported failure.</summary>
        private static void TryDeleteScratch(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* best effort */ }
        }

        /// <summary>Removes the plugin folder when Generate(0) leaves it empty, so a
        /// reduced-to-zero satellite does not leave a dangling Plugins\PyNavisPanes
        /// folder behind.</summary>
        private static void RemoveIfEmpty(string dir)
        {
            try
            {
                if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                    Directory.Delete(dir);
            }
            catch { /* best effort */ }
        }

        /// <summary>PyNavis.dll, the loader: the satellite subclasses PaneSlotBase from it
        /// and is written beside it. Resolved from the already-loaded PyNavis assembly
        /// first, since that is always correct for the process actually running; the
        /// per-user bundle path, then the legacy Plugins path, are only fallbacks for a
        /// context where PyNavis.dll has not loaded yet.</summary>
        private static string LoaderAssemblyPath()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try { name = assembly.GetName().Name; }
                catch { continue; }
                if (string.Equals(name, "PyNavis", StringComparison.OrdinalIgnoreCase) && !assembly.IsDynamic)
                    return assembly.Location;
            }

            var year = Path.GetFileName(NavisDir).Split(' ').Last();
            var bundled = Path.Combine(BundleLayout.DefaultRoot, "Contents", year, "PyNavis.dll");
            if (File.Exists(bundled)) return bundled;
            return Path.Combine(NavisDir, "Plugins", "PyNavis", "PyNavis.dll");
        }

        private static void Save(int extraSlots)
        {
            var path = RuntimeHost.UserConfigPath;
            var config = PyNavisConfig.Load(path);
            PyNavisConfig.SavePaneAssignments(path, config.PaneAssignments, extraSlots);
        }
    }
}
