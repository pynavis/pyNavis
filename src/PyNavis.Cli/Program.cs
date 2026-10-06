using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace PyNavis.Cli
{
    /// <summary>Thin arg-parser over CliCore with the real machine's paths.</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // exe lives in bin\cli\ -> builds live in bin\<year>\
            var binRoot = Path.GetDirectoryName(Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location));
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "pyNavis", "config.json");
            var bundleRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "ApplicationPlugins", "pyNavis.bundle");
            var cli = new CliCore(binRoot, configPath, bundleRoot, ResolveNavisDir, Console.Out);

            try
            {
                switch (args.Length > 0 ? args[0].ToLowerInvariant() : "")
                {
                    case "env":
                        return cli.Env();
                    case "attach" when args.Length == 2:
                        return cli.Attach(args[1]);
                    case "detach" when args.Length == 2:
                        return cli.Detach(args[1]);
                    case "extensions" when args.Length >= 2 && args[1] == "list":
                        return cli.ExtensionsList();
                    case "extensions" when args.Length >= 3 && args[1] == "add":
                        return cli.ExtensionsAdd(args[2]);
                    case "run":
                        return Run(cli, args);
                    case "protect-extension":
                    case "write-manifest":
                        return Upgrade.ExtensionGuard.Command(args, DateTime.Now, Console.Out);
                    default:
                        Console.WriteLine("pynavis - pyNavis command line");
                        Console.WriteLine();
                        Console.WriteLine("  pynavis env                     show detected Navisworks versions and state");
                        Console.WriteLine($"  pynavis attach <year>           put the loader in the per-user bundle ({CliCore.SupportedVersionsNote})");
                        Console.WriteLine("  pynavis detach <year>           remove the loader from the bundle");
                        Console.WriteLine("  pynavis extensions list         show configured extension roots");
                        Console.WriteLine("  pynavis extensions add <dir>    add an extension root to config.json");
                        Console.WriteLine("  pynavis run <script.py> [models]  run a script in a hidden Navisworks (pynavis run --help)");
                        Console.WriteLine("  pynavis protect-extension ...   (the installer's) back up and move your own work");
                        Console.WriteLine("                                  out of pyNavis.extension before an update");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return 1;
            }
        }

        private static int Run(CliCore cli, string[] args)
        {
            // Transcripts are UTF-8 and may hold any text a script wrote; the console's
            // own code page would turn the rest into question marks. No BOM on the stream.
            // The console keeps its code page after the CLI exits, so the user's is put back.
            Encoding previous = null;
            try
            {
                previous = Console.OutputEncoding;
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (IOException) { /* no console to set it on */ }
            try
            {
                return Run(cli, args, previous);
            }
            finally
            {
                RestoreEncoding(previous);
            }
        }

        private static void RestoreEncoding(Encoding previous)
        {
            if (previous == null) return;
            try { Console.OutputEncoding = previous; }
            catch (IOException) { /* no console to set it on */ }
        }

        private static int Run(CliCore cli, string[] args, Encoding previous)
        {
            var runArgs = RunArgs.Parse(args.Skip(1).ToArray(), out var error);
            if (error != null)
            {
                Console.WriteLine(error);
                Console.WriteLine();
                Console.WriteLine(RunArgs.Usage);
                return 2;
            }
            if (runArgs.IsHelp)
            {
                Console.WriteLine(RunArgs.Usage);
                return 0;
            }

            var year = runArgs.Year ?? cli.NewestAttachedYear();
            if (year == null || !cli.IsAttached(year))
            {
                Console.WriteLine(year == null
                    ? "pyNavis is not attached to any Navisworks. Run: pynavis attach <year>"
                    : $"pyNavis is not attached to Navisworks {year}. Run: pynavis attach {year}");
                return 3;
            }
            var navisDir = ResolveNavisDir(year);
            if (navisDir == null || !File.Exists(Path.Combine(navisDir, "Roamer.exe")))
            {
                Console.WriteLine($"Navisworks Manage {year} not found (or is a reference drop without Roamer.exe).");
                return 3;
            }

            var runsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyNavis", "runs");
            var loop = new RunLoop(() => new ReflectionAutomationSession(navisDir),
                runsRoot, Console.Out, () => DateTime.Now,
                roamerPids: RoamerPids, killPid: KillPid);
            // The first Ctrl+C ends the run cleanly, closing the hidden Navisworks instead of
            // leaving it running unseen; a second one ends the CLI at once, skipping every
            // finally, so it puts the console's code page back itself.
            var cancelled = false;
            Console.CancelKeyPress += (sender, e) =>
            {
                if (cancelled)
                {
                    RestoreEncoding(previous);
                    return;
                }
                cancelled = true;
                e.Cancel = true;
                loop.Cancel();
            };
            return loop.Run(runArgs, year);
        }

        // Every running Roamer.exe, so the run can tell the one it started from the user's own.
        private static int[] RoamerPids()
        {
            var processes = Process.GetProcessesByName("Roamer");
            try
            {
                return processes.Select(p => p.Id).ToArray();
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
        }

        // Ends a Roamer by id. False when there is nothing to end: it has exited, or the id
        // now names some other process, which is never touched.
        private static bool KillPid(int pid)
        {
            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    if (!string.Equals(process.ProcessName, "Roamer", StringComparison.OrdinalIgnoreCase)) return false;
                    process.Kill();
                    process.WaitForExit(5000);
                    return true;
                }
            }
            catch (ArgumentException) { return false; }         // no such process: it exited
            catch (InvalidOperationException) { return false; } // it exited before the kill
        }

        private static string ResolveNavisDir(string year)
        {
            int y;
            if (!int.TryParse(year, out y)) return null;
            var regPath = $@"SOFTWARE\Autodesk\Navisworks Manage\{y - 2003}.0\Location";
            using (var key = Registry.LocalMachine.OpenSubKey(regPath))
            {
                var dir = key?.GetValue("Path") as string;
                if (dir != null && Directory.Exists(dir)) return dir.TrimEnd('\\');
            }
            foreach (var root in new[] { @"C:\Program Files", @"D:\Program Files" })
            {
                var candidate = Path.Combine(root, "Autodesk", "Navisworks Manage " + year);
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
