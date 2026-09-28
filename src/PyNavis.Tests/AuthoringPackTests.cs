using System;
using System.IO;
using System.Text.RegularExpressions;
using PyNavis.Runtime.Ai;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The generated authoring pack: the text the model gets as its system prompt and
    /// the text the Copy button hands to people using another assistant.
    /// </summary>
    public class AuthoringPackTests
    {
        private static string RepoPack()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "PyNavis.Runtime", "Ai", "authoring-pack.md");
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("authoring-pack.md not found above the test dir");
        }

        [Fact]
        public void ThePack_ShipsBesideTheRuntime_AndLoads()
        {
            var text = AuthoringPack.Load();

            Assert.True(text.Length > 20000, "pack is suspiciously short: " + text.Length);
            Assert.Contains("pynavis", text);
            Assert.Contains("bundle.yaml", text);
            Assert.Contains("toast", text);
        }

        [Fact]
        public void ThePack_InTheRepo_MatchesTheOneShipped()
        {
            Assert.Equal(File.ReadAllText(RepoPack()), AuthoringPack.Load());
        }

        [Fact]
        public void ThePack_MentionsNoInternalTooling_AndNoEmDashes()
        {
            var text = File.ReadAllText(RepoPack());

            // Built from pieces so this source file itself carries none of the words.
            var banned = string.Join("|", new[]
            {
                "super" + "powers", @"\." + "cla" + @"ude[\\/]", "CLA" + @"UDE\.md", @"SKILL\.md",
            });
            Assert.DoesNotMatch(new Regex(banned, RegexOptions.IgnoreCase), text);
            Assert.DoesNotContain("—", text);
        }

        [Fact]
        public void LoadFrom_AMissingFile_GivesAnEmptyString_NotAnException()
        {
            Assert.Equal("", AuthoringPack.LoadFrom(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".md")));
        }
    }
}
