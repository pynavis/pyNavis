using System;
using PyNavis.Runtime.Units;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// Lengths as people type them into any pyNavis length field: the Revit forms
    /// in a feet-and-inches document, a plain number in the document's unit, and
    /// a metric or imperial value with its unit anywhere. One reader for every
    /// tool (Clear Clash, Section Fit, Section Nudge, Clash Grouper,
    /// Go to Coordinates) and for pynavis.lengths.
    /// </summary>
    public class LengthsTests
    {
        private static void Near(string text, double want, string units = "Feet")
        {
            var got = Lengths.Parse(text, units);
            Assert.True(got.HasValue, $"'{text}' in {units} did not read");
            Assert.True(Math.Abs(got.Value - want) < 1e-9, $"'{text}' in {units}: {got} not {want}");
        }

        [Theory]
        [InlineData("1' 6\"", 1.5)]
        [InlineData("1'6\"", 1.5)]
        [InlineData("1'-6\"", 1.5)]
        [InlineData("1' - 6 1/2\"", 1 + 6.5 / 12)]
        [InlineData("6\"", 0.5)]
        [InlineData("6 1/2\"", 6.5 / 12)]
        [InlineData("3/4\"", 0.75 / 12)]
        [InlineData("1'", 1.0)]
        [InlineData("1.5'", 1.5)]
        [InlineData("6.25\"", 6.25 / 12)]
        [InlineData("  6 \" ", 0.5)]
        [InlineData("1 6", 1.5)]                // Revit's space form: feet, then inches
        [InlineData("1 6 1/2", 1 + 6.5 / 12)]
        [InlineData("0 0 1/8", 1.0 / 96)]
        [InlineData("1-6", 1.5)]                // and its hyphen form
        [InlineData("1-6 1/2", 1 + 6.5 / 12)]
        [InlineData("2", 2.0)]                  // a bare number is in the document's unit
        [InlineData("6 1/2", 6.5)]              // a bare mixed number too
        [InlineData("1/2", 0.5 / 12)]           // but a bare fraction is inches, as in Revit
        [InlineData("0", 0.0)]
        public void Reads_FeetAndFractionalInches_TheWayRevitDoes(string text, double feet) =>
            Near(text, feet);

        [Theory]
        [InlineData("18in", 1.5)]
        [InlineData("1ft 6in", 1.5)]
        [InlineData("2 ft 3 in", 2.25)]
        [InlineData("1 ft 6 1/4 in", 1 + 6.25 / 12)]
        [InlineData("2ft 3in 1/8", 2 + 3.125 / 12)]     // the tools' own readout style
        [InlineData("1 feet 6 inches", 1.5)]
        [InlineData("25mm", 25 / 304.8)]
        [InlineData("2.5 cm", 0.025 / 0.3048)]
        [InlineData("0.1m", 0.1 / 0.3048)]
        [InlineData("6''", 0.5)]                         // two single quotes for inches
        [InlineData("1’ 6”", 1.5)]            // pasted from Word: curly marks
        [InlineData("1′ 6″", 1.5)]            // primes
        public void Reads_WordsUnitsAndPastedMarks(string text, double feet) => Near(text, feet);

        [Theory]
        [InlineData("150", 150.0, "Millimeters")]      // a millimetre document
        [InlineData("150mm", 150.0, "Millimeters")]
        [InlineData("6\"", 152.4, "Millimeters")]
        [InlineData("1' 6\"", 457.2, "Millimeters")]
        [InlineData("0.1m", 100.0, "Millimeters")]
        [InlineData("150mm", 0.15, "Meters")]
        [InlineData("0.5", 0.5, "Meters")]
        [InlineData("6", 6.0, "Inches")]               // an inch document: bare number is inches
        [InlineData("1'", 12.0, "Inches")]
        public void Reads_InOtherDocumentUnits(string text, double want, string units) =>
            Near(text, want, units);

        [Theory]
        [InlineData("", "Feet")]
        [InlineData("   ", "Feet")]
        [InlineData(null, "Feet")]
        [InlineData("abc", "Feet")]
        [InlineData("1'' 2", "Feet")]
        [InlineData("-6\"", "Feet")]
        [InlineData("6\" 1'", "Feet")]
        [InlineData("1/0", "Feet")]
        [InlineData("'", "Feet")]
        [InlineData("6 in 2 ft", "Feet")]
        [InlineData("5 mm 2", "Feet")]
        [InlineData("1 6", "Millimeters")]              // the space form is for feet-and-inches documents
        [InlineData("1-6", "Meters")]
        [InlineData("1 x", "Meters")]
        public void Refuses_WhatItCannotRead(string text, string units) =>
            Assert.Null(Lengths.Parse(text, units));

        [Theory]
        [InlineData(1.5, "Feet", "1' 6\"")]
        [InlineData(3.25 / 12, "Feet", "0' 3 1/4\"")]
        [InlineData(2.0, "Feet", "2' 0\"")]
        [InlineData(0.0, "Feet", "0' 0\"")]
        [InlineData(6.5, "Inches", "0' 6 1/2\"")]
        [InlineData(150.0, "Millimeters", "150")]
        [InlineData(150.5, "Millimeters", "150.5")]
        [InlineData(0.15, "Meters", "0.15")]
        public void FormatInput_WritesWhatAUserWouldType_AndReadsBack(double value, string units, string text)
        {
            Assert.Equal(text, Lengths.FormatInput(value, units));
            Near(Lengths.FormatInput(value, units), value, units);
        }

        [Fact]
        public void Convert_And_Suffix()
        {
            Assert.Equal(304.8, Lengths.Convert(1.0, "Feet", "Millimeters"), 9);
            Assert.Equal(2.0, Lengths.Convert(24.0, "Inches", "Feet"));          // exact, not ...96
            Assert.Equal(2.0, Lengths.Parse("2' 0\"", "Feet"));
            Assert.Equal(2.0, Lengths.Convert(2.0, "Unknown", "Meters"), 9);    // unknown is metres
            Assert.True(Lengths.IsImperial("Feet") && Lengths.IsImperial("Inches"));
            Assert.False(Lengths.IsImperial("Millimeters"));
            // What an input box shows after the number: nothing where the text
            // carries its own marks, the short unit name otherwise.
            Assert.Equal("", Lengths.Suffix("Feet"));
            Assert.Equal("mm", Lengths.Suffix("Millimeters"));
            Assert.Equal("m", Lengths.Suffix("Meters"));
        }
    }
}
