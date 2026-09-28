using System.Collections.Generic;
using System.Linq;
using PyNavis.Runtime.Ai;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The attach-selection payload is the SCHEMA of the selection and nothing else:
    /// how many items, which categories, which property names under each. Never a
    /// value, because a value is project data (a file name, a client, a project
    /// number in a custom tab) and this text leaves the machine.
    /// </summary>
    public class SelectionSummaryTests
    {
        private static PropertyRow Row(string cat, string prop) =>
            new PropertyRow { Category = cat, Property = prop };

        [Fact]
        public void EmptySelection_SaysSo()
        {
            Assert.Equal("Nothing is selected.", SelectionSummary.Build(0, new PropertyRow[0]));
        }

        [Fact]
        public void GroupsPropertyNamesByCategory()
        {
            var rows = new[]
            {
                Row("Element", "Level"), Row("Element", "Level"),
                Row("Element", "Category"), Row("Item", "Type"),
            };

            var text = SelectionSummary.Build(3, rows);

            Assert.StartsWith("3 items selected.", text);
            Assert.Contains("Categories: Element, Item.", text);
            Assert.Contains("\nElement\n  Level\n  Category\n", text);
            Assert.Contains("\nItem\n  Type\n", text);
        }

        [Fact]
        public void ARowCarryingAValue_NeverShowsIt()
        {
            // The C# reader never fills Value any more; if one ever arrives (an old
            // caller, a future reader), it must still not go out.
            var rows = new[] { new PropertyRow { Category = "Item", Property = "Source File", Value = "Tower_Client_Secret.rvt" } };

            var text = SelectionSummary.Build(1, rows);

            Assert.Contains("Source File", text);
            Assert.DoesNotContain("Tower", text);
            Assert.DoesNotContain(":", text.Substring(text.IndexOf('\n')));
        }

        [Fact]
        public void TheWholeThing_IsCapped_AndSaysItWasCut()
        {
            var rows = new List<PropertyRow>();
            for (var c = 0; c < 200; c++)
                for (var p = 0; p < 30; p++)
                    rows.Add(Row("Category" + c, "Property" + p));

            var text = SelectionSummary.Build(500, rows);

            Assert.True(text.Length <= SelectionSummary.MaxChars + 100, "was " + text.Length);
            Assert.EndsWith("(truncated)", text.TrimEnd());
        }

        [Fact]
        public void TheSummary_SaysWhatItIs_SoTheModelDoesNotAskForValues()
        {
            var text = SelectionSummary.Build(2, new[] { Row("Item", "Name") });

            Assert.Contains("names only", text);
        }
    }
}
