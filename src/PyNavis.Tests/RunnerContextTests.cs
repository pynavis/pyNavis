using System;
using System.Collections.Generic;
using System.IO;
using PyNavis.Runtime.Engine;
using PyNavis.Runtime.Execution;
using PyNavis.Runtime.Execution.Runner;
using PyNavis.Runtime.Forms;
using PyNavis.Runtime.Output;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// RunnerContext is process-wide static state, and while it is active every toast,
    /// alert and output write in the process goes to its transcript. These tests enter it,
    /// so they run alone: in parallel with the rest of the suite they would capture other
    /// tests' toasts, and a test asserting "not a runner" could catch one mid-run.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class RunnerContextCollection
    {
        public const string Name = "RunnerContext";
    }

    /// <summary>
    /// The headless half of <c>pynavis run</c>: inside a run nothing may open a window
    /// (there is no one to close it), so toasts, alerts and the output window land in
    /// the transcript, and dialogs that need an answer fail loudly instead of hanging.
    /// </summary>
    [Collection(RunnerContextCollection.Name)]
    public class RunnerContextTests
    {
        private const string DialogsUnavailable =
            "Dialogs are not available under pynavis run. Check __runner__ and skip the prompt.";

        [Fact]
        public void Enter_SetsActiveAndTranscript_DisposeClearsBoth()
        {
            var transcript = new StringWriter();
            Assert.False(RunnerContext.IsActive);
            Assert.Null(RunnerContext.Transcript);

            var scope = RunnerContext.Enter(transcript);
            try
            {
                Assert.True(RunnerContext.IsActive);
                Assert.Same(transcript, RunnerContext.Transcript);
            }
            finally
            {
                scope.Dispose();
            }
            Assert.False(RunnerContext.IsActive);
            Assert.Null(RunnerContext.Transcript);
        }

        [Fact]
        public void Note_WritesLineInsideRun_AndIsNoOpOutside()
        {
            RunnerContext.Note("Before");   // must not throw with no transcript

            var transcript = new StringWriter();
            using (RunnerContext.Enter(transcript))
                RunnerContext.Note("Inside");
            RunnerContext.Note("After");

            Assert.Equal("Inside" + Environment.NewLine, transcript.ToString());
        }

        [Fact]
        public void Enter_WhileActive_Throws_AndLeavesTheFirstRunInPlace()
        {
            var first = new StringWriter();
            using (RunnerContext.Enter(first))
            {
                var ex = Assert.Throws<InvalidOperationException>(() => RunnerContext.Enter(new StringWriter()));
                Assert.Equal("A pynavis run is already active.", ex.Message);
                Assert.True(RunnerContext.IsActive);
                Assert.Same(first, RunnerContext.Transcript);
            }
            Assert.False(RunnerContext.IsActive);
        }

        /// <summary>A transcript whose every write fails, like a disposed writer or a full disk.</summary>
        private sealed class FailingWriter : TextWriter
        {
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
            public override void Write(char value) => throw new IOException("Disk full.");
            public override void Write(string value) => throw new IOException("Disk full.");
            public override void WriteLine(string value) => throw new IOException("Disk full.");
        }

        [Fact]
        public void Note_SwallowsTranscriptFailures_SoToastAndAlertNeverThrow()
        {
            using (RunnerContext.Enter(new FailingWriter()))
            {
                RunnerContext.Note("Lost");
                Toast.Show("info", "Hello", "detail");
                Dialogs.Alert("Nothing selected.", "Count");
            }

            var disposed = new StringWriter();
            disposed.Dispose();
            using (RunnerContext.Enter(disposed))
                Toast.Show("error", "Failed", null);
        }

        [Fact]
        public void Host_IsRunner_FollowsContext()
        {
            Assert.False(PyNavisHost.Instance.IsRunner);
            using (RunnerContext.Enter(new StringWriter()))
                Assert.True(PyNavisHost.Instance.IsRunner);
            Assert.False(PyNavisHost.Instance.IsRunner);
        }

        [Fact]
        public void Toast_InsideRun_WritesTranscriptLine_InsteadOfAWindow()
        {
            // xunit runs MTA: had the toast gone for a window it would have failed (and
            // Show swallows that), so the transcript line is the proof it never tried.
            var transcript = new StringWriter();
            using (RunnerContext.Enter(transcript))
            {
                Toast.Show("info", "Hello", "detail");
                Toast.Show("success", "Done", null);
            }

            Assert.Equal(
                "[info] Hello: detail" + Environment.NewLine +
                "[success] Done" + Environment.NewLine,
                transcript.ToString());
        }

        [Fact]
        public void Banner_InsideRun_WritesTranscriptLine_InsteadOfAWindow()
        {
            var transcript = new StringWriter();
            using (RunnerContext.Enter(transcript))
            {
                Banner.Show("success", "12.5 mm", "Gap");
                Banner.Show("info", "Pick the first point", null, 0);
            }

            Assert.Equal(
                "[banner] 12.5 mm: Gap" + Environment.NewLine +
                "[banner] Pick the first point" + Environment.NewLine,
                transcript.ToString());
        }

        [Fact]
        public void Alert_InsideRun_WritesTranscriptLine()
        {
            var transcript = new StringWriter();
            using (RunnerContext.Enter(transcript))
            {
                Dialogs.Alert("Nothing selected.", "Count");
                Dialogs.Alert("With copy.", "Copy", "copied text");
            }

            Assert.Equal(
                "[alert] Count: Nothing selected." + Environment.NewLine +
                "[alert] Copy: With copy." + Environment.NewLine,
                transcript.ToString());
        }

        [Fact]
        public void PromptingDialogs_InsideRun_Throw()
        {
            using (RunnerContext.Enter(new StringWriter()))
            {
                Assert.Equal(DialogsUnavailable,
                    Assert.Throws<InvalidOperationException>(() => Dialogs.Confirm("Delete?", "Delete")).Message);
                Assert.Equal(DialogsUnavailable,
                    Assert.Throws<InvalidOperationException>(() => Dialogs.AskString("Name?", "a", "Name")).Message);
                Assert.Equal(DialogsUnavailable,
                    Assert.Throws<InvalidOperationException>(() => Dialogs.SaveFile("CSV|*.csv", "out.csv", "Save")).Message);
                Assert.Equal(DialogsUnavailable,
                    Assert.Throws<InvalidOperationException>(() => Dialogs.OpenFile("CSV|*.csv", "Open")).Message);
            }
        }

        public static TheoryData<string> PickerEntryPoints => new TheoryData<string>
        {
            "SelectFromList", "CommandSwitch", "AskNumber", "FolderPicker",
        };

        [Theory]
        [MemberData(nameof(PickerEntryPoints))]
        public void Pickers_InsideRun_Throw(string entryPoint)
        {
            Action call;
            switch (entryPoint)
            {
                case "SelectFromList": call = () => Pickers.SelectFromList("Pick", new[] { "a", "b" }, false, "Choose"); break;
                case "CommandSwitch": call = () => Pickers.CommandSwitch("Mode", "Choose", new[] { "One", "Two" }); break;
                case "AskNumber": call = () => Pickers.AskNumber("How many?", 0, 10, 5); break;
                case "FolderPicker": call = () => FolderPicker.Pick("Folder", null); break;
                default: throw new ArgumentOutOfRangeException(nameof(entryPoint));
            }

            using (RunnerContext.Enter(new StringWriter()))
                Assert.Equal(DialogsUnavailable, Assert.Throws<InvalidOperationException>(call).Message);
        }

        public static TheoryData<string> ViewpointDialogEntryPoints => new TheoryData<string>
        {
            "Deleter", "Reset", "Renamer", "Manager",
        };

        /// <summary>The viewpoint tools are modal too: under a run each must refuse before
        /// building anything. xunit runs MTA, so a dialog that got as far as building its
        /// window would fail with WPF's STA error instead of the dialogs message.</summary>
        [Theory]
        [MemberData(nameof(ViewpointDialogEntryPoints))]
        public void ViewpointDialogs_InsideRun_Throw(string entryPoint)
        {
            var rows = new List<ViewpointRow>
            {
                new ViewpointRow { Guid = "a", Key = "0", ParentKey = "", Folder = "", Name = "North", Kind = "viewpoint" },
            };
            Action call;
            switch (entryPoint)
            {
                case "Deleter": call = () => ViewpointDeleterDialog.Show(rows); break;
                case "Reset": call = () => ViewpointResetDialog.Show(rows, "30 m/s"); break;
                case "Renamer": call = () => ViewpointRenamerDialog.Show(rows, request => new ViewpointRenamerDialog.RenamePreview()); break;
                case "Manager": call = () => ViewpointManagerDialog.Show(rows, action => new ViewpointManagerDialog.ManagerResult()); break;
                default: throw new ArgumentOutOfRangeException(nameof(entryPoint));
            }

            using (RunnerContext.Enter(new StringWriter()))
                Assert.Equal(DialogsUnavailable, Assert.Throws<InvalidOperationException>(call).Message);
        }

        [Fact]
        public void ClashGrouper_InsideRun_Throws_InsteadOfWaitingOnAnInvisibleDialog()
        {
            // The grouper is modal like the pickers: under pynavis run nobody can
            // click Apply or Cancel, so it must fail loudly, not hang the host.
            using (RunnerContext.Enter(new StringWriter()))
                Assert.Equal(DialogsUnavailable, Assert.Throws<InvalidOperationException>(() =>
                    ClashGrouperDialog.Show(
                        new System.Collections.Generic.List<ClashGrouperDialog.TestRow>(),
                        new System.Collections.Generic.List<ClashGrouperDialog.RuleChoice>(),
                        null)).Message);
        }

        [Fact]
        public void OutputWindowWriter_InsideRun_WritesTranscriptInOrder()
        {
            var transcript = new StringWriter();
            using (RunnerContext.Enter(transcript))
            {
                var writer = new OutputWindowWriter("Count");
                Assert.False(writer.HasOpenWindow);

                writer.Write("hi");
                writer.WriteError("bad");
                writer.WriteHtml("<b>x</b>");

                Assert.True(writer.HasOpenWindow);
                Assert.Equal("item", writer.ElementLink(new object(), "item"));
                writer.Progress(0.5, "Half");   // headless: writes nothing, must not throw
            }

            Assert.Equal("hiERROR: bad<b>x</b>", transcript.ToString());
        }

        [Fact]
        public void TranscriptOutputWindow_HonoursTheWindowContract()
        {
            var transcript = new StringWriter();
            IOutputWindow window = new TranscriptOutputWindow(transcript);

            window.Show();
            window.SetTitle("Ignored");
            window.AppendText("plain", false);
            window.AppendText("oops", true);
            window.AppendHtml("<i>y</i>");
            window.ShowProgress(1.0, "Done");

            Assert.Equal("plainERROR: oops<i>y</i>", transcript.ToString());
            Assert.Equal("label", window.RegisterElementLink(null, "label"));
            Assert.NotNull(window.Dispatcher);
        }

        // ---- pynavis.script.is_runner ------------------------------------------

        private static IronPythonEngine NewEngine()
        {
            var engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            engine.Initialize(config);
            return engine;
        }

        private static string RunIsRunner(IronPythonEngine engine)
        {
            var output = new StringWriter();
            var result = engine.Execute(new ScriptRequest
            {
                Code = "from pynavis import script\nprint('runner=' + str(script.is_runner()))",
                Output = output,
            });
            Assert.True(result.Succeeded, result.ErrorText);
            return output.ToString();
        }

        [Fact]
        public void ScriptIsRunner_IsFalseOutsideARun_AndTrueInside()
        {
            var engine = NewEngine();

            Assert.Contains("runner=False", RunIsRunner(engine));
            using (RunnerContext.Enter(new StringWriter()))
                Assert.Contains("runner=True", RunIsRunner(engine));
        }

        [Fact]
        public void WpfWindowShowDialog_InsideRun_RaisesBeforeAnyWindowWork()
        {
            // The window is a stand-in: theming or showing it would fail with a different
            // error, so the dialogs message proves the guard runs first.
            const string code =
                "from pynavis import forms\n" +
                "class FakeWindow(object):\n" +
                "    def ShowDialog(self):\n" +
                "        raise AssertionError('window shown')\n" +
                "w = forms.WPFWindow.__new__(forms.WPFWindow)\n" +
                "w.window = FakeWindow()\n" +
                "try:\n" +
                "    w.show_dialog()\n" +
                "    print('no-raise')\n" +
                "except Exception as ex:\n" +
                "    print('raised=' + str(ex))\n";
            var engine = NewEngine();
            var output = new StringWriter();
            ExecResult result;
            using (RunnerContext.Enter(new StringWriter()))
                result = engine.Execute(new ScriptRequest { Code = code, Output = output });

            Assert.True(result.Succeeded, result.ErrorText);
            Assert.Contains("raised=" + DialogsUnavailable, output.ToString());
        }
    }
}
