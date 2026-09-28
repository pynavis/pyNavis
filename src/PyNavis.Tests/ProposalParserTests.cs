using PyNavis.Runtime.Ai;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Turning a model reply into files. The format is fenced blocks named by file,
    /// and the parser is the only thing standing between the model's output and the
    /// user's disk, so it must refuse every path that is not one of the three files.
    /// </summary>
    public class ProposalParserTests
    {
        private const string Reply =
            "Here is a tool that isolates the selection's level.\n\n" +
            "Title: Isolate Level\n" +
            "Icon: IL teal\n\n" +
            "```yaml bundle.yaml\ntitle: Isolate Level\ntooltip: Hides everything not on the selected level.\n```\n\n" +
            "```python script.py\nfrom pynavis import selection, toast\ntoast.info('hi')\n```\n\n" +
            "Shift+Click opens the options.\n\n" +
            "```python config.py\nfrom pynavis import forms\n```\n";

        [Fact]
        public void Parses_TheThreeFiles_ByTheirFenceNames()
        {
            var p = ProposalParser.Parse(Reply);

            Assert.True(p.IsComplete);
            Assert.Equal(3, p.Files.Count);
            Assert.StartsWith("title: Isolate Level", p.Files["bundle.yaml"]);
            Assert.StartsWith("from pynavis import selection", p.Files["script.py"]);
            Assert.StartsWith("from pynavis import forms", p.Files["config.py"]);
        }

        [Fact]
        public void Title_ComesFromBundleYaml_First()
        {
            var p = ProposalParser.Parse(Reply.Replace("Title: Isolate Level\n", "Title: Something Else\n"));

            Assert.Equal("Isolate Level", p.Title);
        }

        [Fact]
        public void Title_FallsBackToTheTitleLine_WhenBundleYamlHasNone()
        {
            var p = ProposalParser.Parse(Reply.Replace("title: Isolate Level\n", ""));

            Assert.Equal("Isolate Level", p.Title);
        }

        [Fact]
        public void Icon_ReadsLettersAndColour_AndDefaultsFromTheTitle()
        {
            var p = ProposalParser.Parse(Reply);
            Assert.Equal("IL", p.Icon.Letters);
            Assert.Equal("teal", p.Icon.Colour);

            var noIcon = ProposalParser.Parse(Reply.Replace("Icon: IL teal\n", ""));
            Assert.Equal("IL", noIcon.Icon.Letters);           // initials of "Isolate Level"
            Assert.Equal(IconSpec.DefaultColour, noIcon.Icon.Colour);
        }

        [Fact]
        public void Icon_WithAnUnknownColour_OrTooManyLetters_IsTamed()
        {
            var p = ProposalParser.Parse(Reply.Replace("Icon: IL teal", "Icon: isolate blue"));

            Assert.Equal("IS", p.Icon.Letters);
            Assert.Equal(IconSpec.DefaultColour, p.Icon.Colour);
        }

        [Fact]
        public void Explanation_IsTheProseOutsideTheFences_WithoutTheTitleAndIconLines()
        {
            var p = ProposalParser.Parse(Reply);

            Assert.Contains("isolates the selection's level", p.Explanation);
            Assert.Contains("Shift+Click opens the options.", p.Explanation);
            Assert.DoesNotContain("Title:", p.Explanation);
            Assert.DoesNotContain("Icon:", p.Explanation);
            Assert.DoesNotContain("toast.info", p.Explanation);
        }

        [Fact]
        public void AFileNamedOnTheLineBeforeTheFence_IsAccepted_Too()
        {
            var reply = "**script.py**\n```python\nprint(1)\n```\n\nFile: bundle.yaml\n```\ntitle: T\n```\n";

            var p = ProposalParser.Parse(reply);

            Assert.Equal("print(1)", p.Files["script.py"].Trim());
            Assert.Equal("title: T", p.Files["bundle.yaml"].Trim());
        }

        [Fact]
        public void AnyOtherPath_IsRefused_AndListed_NeverWritten()
        {
            var reply = "```python ../../evil.py\nx\n```\n```python lib/helper.py\ny\n```\n```python script.py\nz\n```\n";

            var p = ProposalParser.Parse(reply);

            Assert.Single(p.Files);
            Assert.Equal(new[] { "../../evil.py", "lib/helper.py" }, p.RefusedFiles);
        }

        [Fact]
        public void AReplyWithNoScript_IsNotComplete_ButKeepsItsProse()
        {
            var p = ProposalParser.Parse("I need to know which level property you mean. Item or Element?");

            Assert.False(p.IsComplete);
            Assert.Empty(p.Files);
            Assert.Contains("which level property", p.Explanation);
        }

        [Fact]
        public void AnUnnamedFence_WhenItIsTheOnlyOne_AndLooksLikePython_IsTheScript()
        {
            var p = ProposalParser.Parse("Title: Ping\n```python\nfrom pynavis import toast\ntoast.info('pong')\n```\n");

            Assert.True(p.IsComplete);
            Assert.Contains("toast.info", p.Files["script.py"]);
        }

        [Fact]
        public void TildeFences_AndCrLf_Parse_Too()
        {
            var p = ProposalParser.Parse("Title: T\r\n~~~python script.py\r\nx = 1\r\n~~~\r\n");

            Assert.Equal("x = 1", p.Files["script.py"].Trim());
        }
    }
}
