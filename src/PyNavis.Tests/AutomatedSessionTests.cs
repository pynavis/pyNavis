using System;
using System.IO;
using System.Text.RegularExpressions;
using PyNavis.Runtime.Execution.Runner;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// A Navisworks that pynavis run started (Application.IsAutomated) gets no ribbon, no
    /// hooks, no notices and no update check. Reading the flag needs Navisworks, so the
    /// part that decides is tested here and Boot's use of it is checked on the source.
    /// </summary>
    public class AutomatedSessionTests
    {
        [Fact]
        public void AnAutomatedSession_QueuesOnlyALogLine_NeverTheStages()
        {
            Action queued = null;
            var stagesRan = false;

            var result = AutomatedSession.QueueInteractiveStages(true, a => queued = a, () => stagesRan = true);

            Assert.False(result);
            Assert.NotNull(queued);          // the ribbon-ready moment is still observed
            queued();                        // and running it does not run the stages
            Assert.False(stagesRan);
        }

        [Fact]
        public void AnInteractiveSession_QueuesTheStages_ForWhenTheRibbonIsReady()
        {
            Action queued = null;
            var ran = false;

            var result = AutomatedSession.QueueInteractiveStages(false, stages => queued = stages, () => ran = true);

            Assert.True(result);
            Assert.NotNull(queued);
            Assert.False(ran);               // queued, not run
            queued();
            Assert.True(ran);
        }

        [Theory]
        [InlineData(new[] { "Roamer.exe", "-Embedding" }, true)]
        [InlineData(new[] { "Roamer.exe", "/embedding" }, true)]
        [InlineData(new[] { "Roamer.exe" }, false)]
        [InlineData(new[] { "Roamer.exe", "Site.nwd" }, false)]
        [InlineData(new[] { "Roamer.exe", "-EmbeddingSomething" }, false)]
        [InlineData(new[] { "Roamer.exe", "Embedding" }, false)]
        public void StartedByCom_RecognisesOnlyTheEmbeddingSwitch(string[] args, bool expected)
        {
            // Field-measured 2026-10-04: Application.IsAutomated is False in a Navisworks the
            // Automation API started, so the -Embedding switch COM adds is what decides.
            Assert.Equal(expected, AutomatedSession.StartedByCom(args));
        }

        [Fact]
        public void StartedByCom_IsFalse_ForNullOrEmptyArgs()
        {
            Assert.False(AutomatedSession.StartedByCom(null));
            Assert.False(AutomatedSession.StartedByCom(new string[0]));
        }

        [Fact]
        public void TheSkipMessage_NamesWhatIsSkipped()
        {
            Assert.Equal("Automated session: ribbon, hooks, notices and update check skipped.", AutomatedSession.SkipMessage);
        }

        [Fact]
        public void Boot_ReadsIsAutomated_AndQueuesTheRibbonOnlyThroughTheGate()
        {
            string source = null;
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "PyNavis.Runtime", "RuntimeHost.cs");
                if (File.Exists(candidate)) { source = File.ReadAllText(candidate); break; }
            }
            Assert.NotNull(source);

            var boot = Regex.Match(source, @"public static void Boot\(\)(.*?)public static void EnsureStartupScriptsRan", RegexOptions.Singleline);
            Assert.True(boot.Success, "Boot not found in RuntimeHost.cs");
            var body = boot.Groups[1].Value;
            Assert.Contains("AutomatedSession.Detect()", body);
            Assert.Contains("AutomatedSession.QueueInteractiveStages(automated, RibbonBootstrap.WhenRibbonReady,", body);
            Assert.DoesNotContain("RibbonBootstrap.WhenRibbonReady(", body);
            // Detect comes before anything is queued.
            Assert.True(body.IndexOf("AutomatedSession.Detect()", StringComparison.Ordinal)
                        < body.IndexOf("QueueInteractiveStages", StringComparison.Ordinal));
        }
    }
}
