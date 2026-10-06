using PyNavis.Cli;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>The argument rules of <c>pynavis run</c>.</summary>
    public class RunArgsTests
    {
        [Fact]
        public void ScriptThenTwoModels_ArePositional()
        {
            var args = RunArgs.Parse(new[] { "audit.py", "a.nwd", "b.nwd" }, out var error);

            Assert.Null(error);
            Assert.Equal("audit.py", args.Script);
            Assert.Equal(new[] { "a.nwd", "b.nwd" }, args.Models);
            Assert.Empty(args.ScriptArgs);
            Assert.False(args.Save);
            Assert.False(args.Show);
            Assert.Equal(0, args.TimeoutSeconds);
            Assert.Null(args.Year);
            Assert.Null(args.ModelsFile);
            Assert.Null(args.ResultsPath);
            Assert.False(args.IsHelp);
        }

        [Fact]
        public void EveryFlag_IsRead_WhereverItSits()
        {
            var args = RunArgs.Parse(new[]
            {
                "--save", "audit.py", "--models", "list.txt", "a.nwd", "--version", "2025",
                "--show", "--timeout", "90", "--results", "out.json",
            }, out var error);

            Assert.Null(error);
            Assert.Equal("audit.py", args.Script);
            Assert.Equal(new[] { "a.nwd" }, args.Models);
            Assert.Equal("list.txt", args.ModelsFile);
            Assert.Equal("2025", args.Year);
            Assert.True(args.Save);
            Assert.True(args.Show);
            Assert.Equal(90, args.TimeoutSeconds);
            Assert.Equal("out.json", args.ResultsPath);
        }

        [Fact]
        public void DoubleDash_EndsFlagParsing_AndTheRestGoToTheScript()
        {
            var args = RunArgs.Parse(new[] { "audit.py", "a.nwd", "--", "--save", "x", "--" }, out var error);

            Assert.Null(error);
            Assert.Equal(new[] { "a.nwd" }, args.Models);
            Assert.False(args.Save);
            Assert.Equal(new[] { "--save", "x", "--" }, args.ScriptArgs);
        }

        [Fact]
        public void NoScript_IsAnError()
        {
            RunArgs.Parse(new[] { "--save" }, out var error);
            Assert.Equal("Give the script to run.", error);

            RunArgs.Parse(new string[0], out error);
            Assert.Equal("Give the script to run.", error);
        }

        [Fact]
        public void UnknownOption_IsAnError()
        {
            RunArgs.Parse(new[] { "audit.py", "--frobnicate" }, out var error);
            Assert.Equal("Unknown option: --frobnicate", error);
        }

        [Fact]
        public void MissingValue_IsAnError()
        {
            RunArgs.Parse(new[] { "audit.py", "--models" }, out var error);
            Assert.Equal("Option --models needs a value.", error);

            RunArgs.Parse(new[] { "audit.py", "--results", "--save" }, out error);
            Assert.Equal("Option --results needs a value.", error);
        }

        [Fact]
        public void NonNumericTimeoutOrVersion_IsAnError()
        {
            RunArgs.Parse(new[] { "audit.py", "--timeout", "abc" }, out var error);
            Assert.NotNull(error);
            Assert.Contains("--timeout", error);

            RunArgs.Parse(new[] { "audit.py", "--timeout", "-5" }, out error);
            Assert.NotNull(error);

            RunArgs.Parse(new[] { "audit.py", "--timeout", "2147484" }, out error);
            Assert.Equal("Option --timeout is too large.", error);

            Assert.Equal(2147483, RunArgs.Parse(new[] { "audit.py", "--timeout", "2147483" }, out error).TimeoutSeconds);
            Assert.Null(error);

            RunArgs.Parse(new[] { "audit.py", "--version", "latest" }, out error);
            Assert.NotNull(error);
            Assert.Contains("--version", error);
        }

        [Fact]
        public void Help_IsDetected_WithoutAScript()
        {
            var args = RunArgs.Parse(new[] { "--help" }, out var error);

            Assert.Null(error);
            Assert.True(args.IsHelp);
        }

        [Fact]
        public void Usage_ListsEveryOption()
        {
            Assert.StartsWith("pynavis run <script.py> [model ...] [options] [-- script args]", RunArgs.Usage);
            foreach (var flag in new[] { "--models <list.txt>", "--version <year>", "--save", "--show", "--timeout <seconds>", "--results <file.json>" })
                Assert.Contains(flag, RunArgs.Usage);
        }

        [Fact]
        public void Usage_SaysOnlyNavisworksFilesAreSaved()
        {
            Assert.Contains("--save                 save each .nwd or .nwf model back to its own path after the script", RunArgs.Usage);
        }
    }
}
