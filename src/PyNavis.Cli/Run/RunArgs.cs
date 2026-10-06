using System;
using System.Collections.Generic;
using System.Globalization;

namespace PyNavis.Cli
{
    /// <summary>
    /// The command line of <c>pynavis run</c>: the first plain word is the script, later
    /// plain words are models, and everything after <c>--</c> goes to the script untouched.
    /// </summary>
    public sealed class RunArgs
    {
        /// <summary>What <c>pynavis run --help</c> prints, and what follows a usage error.</summary>
        public const string Usage =
            "pynavis run <script.py> [model ...] [options] [-- script args]\n" +
            "\n" +
            "  --models <list.txt>    one model path per line, added to the positional ones\n" +
            "  --version <year>       Navisworks year; default is the newest attached one\n" +
            "  --save                 save each .nwd or .nwf model back to its own path after the script\n" +
            "  --show                 show the Navisworks window while each script runs\n" +
            "  --timeout <seconds>    give up on a model after this long; default none\n" +
            "  --results <file.json>  copy the run summary here as well";

        /// <summary>The .py file to run, as given.</summary>
        public string Script { get; private set; }

        /// <summary>The positional model paths, as given.</summary>
        public List<string> Models { get; } = new List<string>();

        /// <summary>The <c>--models</c> list file, or null.</summary>
        public string ModelsFile { get; private set; }

        /// <summary>The <c>--version</c> year, or null for the newest attached one.</summary>
        public string Year { get; private set; }

        /// <summary>Save each model back to its own path after a script that succeeded.</summary>
        public bool Save { get; private set; }

        /// <summary>Show the Navisworks window instead of running hidden.</summary>
        public bool Show { get; private set; }

        /// <summary>Seconds to wait for one model's script; 0 waits without limit.</summary>
        public int TimeoutSeconds { get; private set; }

        /// <summary>Where to copy results.json as well, or null.</summary>
        public string ResultsPath { get; private set; }

        /// <summary>The arguments after <c>--</c>, handed to the script.</summary>
        public List<string> ScriptArgs { get; } = new List<string>();

        /// <summary>True when <c>--help</c> was asked for; nothing else is checked then.</summary>
        public bool IsHelp { get; private set; }

        /// <summary>
        /// Reads the words after <c>run</c>. Returns null and sets <paramref name="error"/>
        /// to a one-line message when they make no sense; <paramref name="error"/> is null otherwise.
        /// </summary>
        public static RunArgs Parse(string[] argsAfterRun, out string error)
        {
            var result = new RunArgs();
            error = null;
            for (var i = 0; i < argsAfterRun.Length; i++)
            {
                var arg = argsAfterRun[i];
                if (arg == "--")
                {
                    for (i++; i < argsAfterRun.Length; i++) result.ScriptArgs.Add(argsAfterRun[i]);
                    break;
                }
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    if (result.Script == null) result.Script = arg;
                    else result.Models.Add(arg);
                    continue;
                }

                switch (arg)
                {
                    case "--help": result.IsHelp = true; return result;
                    case "--save": result.Save = true; continue;
                    case "--show": result.Show = true; continue;
                    case "--models":
                    case "--version":
                    case "--timeout":
                    case "--results":
                        break;
                    default:
                        error = "Unknown option: " + arg;
                        return null;
                }

                if (i + 1 >= argsAfterRun.Length || argsAfterRun[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    error = $"Option {arg} needs a value.";
                    return null;
                }
                var value = argsAfterRun[++i];
                int number;
                var isNumber = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
                switch (arg)
                {
                    case "--models": result.ModelsFile = value; break;
                    case "--results": result.ResultsPath = value; break;
                    case "--version" when !isNumber:
                        error = $"Option --version needs a year, like 2026, not {value}.";
                        return null;
                    case "--version": result.Year = value; break;
                    case "--timeout" when !isNumber:
                        error = $"Option --timeout needs a whole number of seconds, not {value}.";
                        return null;
                    case "--timeout" when number > int.MaxValue / 1000: // the wait is in milliseconds
                        error = "Option --timeout is too large.";
                        return null;
                    case "--timeout": result.TimeoutSeconds = number; break;
                }
            }

            if (result.Script == null)
            {
                error = "Give the script to run.";
                return null;
            }
            return result;
        }
    }
}
