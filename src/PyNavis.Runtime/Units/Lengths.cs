using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PyNavis.Runtime.Units
{
    /// <summary>
    /// Lengths as people type them, for every pyNavis length field: Clear Clash,
    /// Section Fit, Section Nudge, the Clash Grouper and Go to Coordinates,
    /// and scripts through pynavis.lengths. One reader, so a length that works in
    /// one tool works in all of them.
    ///
    /// In a feet-and-inches document it reads what Revit does: 1' 6", 1'-6 1/2",
    /// 6 1/2", 3/4", 1.5', the space form 1 6 and the hyphen form 1-6 (feet, then
    /// inches), and a bare fraction as inches. Anywhere, a bare number is in the
    /// document's own unit, and a value with its unit (25mm, 0.1m, 6", 1' 6") is
    /// converted. Pure, invariant culture throughout.
    /// </summary>
    public static class Lengths
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly Dictionary<string, double> MetersPerUnit =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Meters"] = 1.0, ["Centimeters"] = 0.01, ["Millimeters"] = 0.001,
                ["Kilometers"] = 1000.0, ["Feet"] = 0.3048, ["Inches"] = 0.0254,
                ["Yards"] = 0.9144, ["Miles"] = 1609.344, ["Micrometers"] = 1e-6,
                ["Microinches"] = 2.54e-8, ["Mils"] = 2.54e-5,
            };

        // Document units that read naturally as feet and inches, with inches per unit.
        private static readonly Dictionary<string, double> InchesPerUnit =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Feet"] = 12.0, ["Inches"] = 1.0, ["Yards"] = 36.0, ["Miles"] = 63360.0,
            };

        private static readonly Dictionary<string, string> Suffixes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Meters"] = "m", ["Centimeters"] = "cm", ["Millimeters"] = "mm",
                ["Kilometers"] = "km", ["Micrometers"] = "um", ["Mils"] = "mil",
                ["Microinches"] = "uin",
            };

        private static readonly Dictionary<string, string> MetricWords =
            new Dictionary<string, string> { ["mm"] = "Millimeters", ["cm"] = "Centimeters", ["m"] = "Meters" };

        private const string Number = @"(?:\d+(?:\.\d*)?|\.\d+)";
        private const string Fraction = @"\d+\s*/\s*\d+";
        private const string Mixed = "(?:" + Number + @"\s+" + Fraction + "|" + Fraction + "|" + Number + ")";
        private const string FeetMark = "(?:'|feet|foot|ft)";
        private const string InchMark = "(?:\"|inches|inch|in)";

        private static Regex Whole(string pattern) =>
            new Regex("^" + pattern + "$", RegexOptions.CultureInvariant);

        // 1' 6", 1'-6 1/2", 1ft 6in, 2ft 3in 1/8 (the tools' own readout style)
        private static readonly Regex FeetInches = Whole(
            "(?<ft>" + Mixed + @")\s*" + FeetMark +
            @"(?:\s*-?\s*(?<in>" + Mixed + @")\s*(?:" + InchMark + @")?\s*(?<tail>" + Fraction + ")?)?");

        // 6", 6 1/2", 3/4", 18in
        private static readonly Regex InchesOnly = Whole(
            "(?<in>" + Mixed + @")\s*" + InchMark + @"\s*(?<tail>" + Fraction + ")?");

        private static readonly Regex Metric = Whole("(?<v>" + Number + @")\s*(?<u>mm|cm|m)");

        // Feet-and-inches documents only: 1 6, 1 6 1/2, 0 0 1/8, 1-6, 1-6 1/2
        private static readonly Regex Spaced = Whole(
            "(?<ft>" + Number + @")\s+(?<in>" + Number + @"(?:\s+" + Fraction + ")?)");
        private static readonly Regex Hyphen = Whole("(?<ft>" + Number + @")\s*-\s*(?<in>" + Mixed + ")");
        private static readonly Regex BareFraction = Whole("(?<f>" + Fraction + ")");

        private static readonly Regex Bare = Whole("(?<v>" + Mixed + ")");

        /// <summary>True for document units that read as feet and inches.</summary>
        public static bool IsImperial(string units) => units != null && InchesPerUnit.ContainsKey(units);

        /// <summary>A length in one Navisworks unit name expressed in another; unknown
        /// names count as metres. Between two imperial units it goes by inches, so
        /// 24 inches is exactly 2 feet rather than 1.9999999999999996.</summary>
        public static double Convert(double value, string fromUnits, string toUnits)
        {
            if (IsImperial(fromUnits) && IsImperial(toUnits))
                return value * InchesPerUnit[fromUnits] / InchesPerUnit[toUnits];
            return value * Meters(fromUnits) / Meters(toUnits);
        }

        private static double Meters(string units) =>
            units != null && MetersPerUnit.TryGetValue(units, out var m) ? m : 1.0;

        /// <summary>What an input box shows after the number: nothing in a
        /// feet-and-inches document, whose text carries its own marks, and the short
        /// unit name otherwise.</summary>
        public static string Suffix(string units)
        {
            if (IsImperial(units)) return "";
            return units != null && Suffixes.TryGetValue(units, out var s) ? s : (units ?? "");
        }

        /// <summary>A typed length in the document's units, or null when it does not
        /// read as one. Negative lengths are refused: there is no field they belong in.</summary>
        public static double? Parse(string text, string units)
        {
            if (text == null) return null;
            var t = Normalize(text);
            if (t.Length == 0) return null;
            try
            {
                var m = FeetInches.Match(t);
                if (!m.Success) m = InchesOnly.Match(t);
                if (m.Success)
                    return FromInches(FeetOf(m) * 12.0 + InchesOf(m), units);

                m = Metric.Match(t);
                if (m.Success)
                    return Convert(double.Parse(m.Groups["v"].Value, NumberStyles.Float, Inv),
                        MetricWords[m.Groups["u"].Value], units);

                if (IsImperial(units))
                {
                    m = Spaced.Match(t);
                    if (!m.Success) m = Hyphen.Match(t);
                    if (m.Success)
                        return FromInches(FeetOf(m) * 12.0 + InchesOf(m), units);
                    m = BareFraction.Match(t);
                    if (m.Success)
                        return FromInches(MixedValue(m.Groups["f"].Value), units);
                }

                m = Bare.Match(t);
                if (m.Success)
                    return MixedValue(m.Groups["v"].Value);
            }
            catch (DivideByZeroException) { }
            catch (FormatException) { }
            catch (OverflowException) { }
            return null;
        }

        /// <summary>A length in the document's units as a user would type it back:
        /// 1' 6 1/2" in a feet-and-inches document (to the nearest 1/denominator of an
        /// inch), a plain decimal otherwise. Parse reads it back.</summary>
        public static string FormatInput(double value, string units, int denominator = 16)
        {
            if (!InchesPerUnit.TryGetValue(units ?? "", out var perUnit))
                return value.ToString("0.######", Inv);
            denominator = Math.Max(1, denominator);
            var sign = value < 0 ? "-" : "";
            var ticks = (long)Math.Round(Math.Abs(value) * perUnit * denominator, MidpointRounding.AwayFromZero);
            var feet = ticks / (12 * denominator);
            var rest = ticks % (12 * denominator);
            var inches = rest / denominator;
            var num = rest % denominator;
            var text = string.Format(Inv, "{0}{1}' {2}", sign, feet, inches);
            if (num != 0)
            {
                var g = Gcd(num, denominator);
                text += string.Format(Inv, " {0}/{1}", num / g, denominator / g);
            }
            return text + "\"";
        }

        // ---- helpers -------------------------------------------------------------

        private static string Normalize(string text)
        {
            var t = text.Trim().ToLowerInvariant()
                .Replace('’', '\'').Replace('′', '\'')
                .Replace('”', '"').Replace('“', '"').Replace('″', '"');
            return t.Replace("''", "\"");
        }

        private static double FeetOf(Match m) =>
            m.Groups["ft"].Success ? MixedValue(m.Groups["ft"].Value) : 0.0;

        private static double InchesOf(Match m) =>
            (m.Groups["in"].Success ? MixedValue(m.Groups["in"].Value) : 0.0)
            + (m.Groups["tail"].Success ? MixedValue(m.Groups["tail"].Value) : 0.0);

        private static double FromInches(double inches, string units) => Convert(inches, "Inches", units);

        /// <summary>6.5, 1/2 or 6 1/2 as a number.</summary>
        private static double MixedValue(string text)
        {
            text = text.Trim();
            var slash = text.LastIndexOf('/');
            if (slash < 0) return double.Parse(text, NumberStyles.Float, Inv);
            var den = double.Parse(text.Substring(slash + 1).Trim(), NumberStyles.Float, Inv);
            if (den == 0) throw new DivideByZeroException();
            var head = text.Substring(0, slash).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var whole = head.Length == 2 ? double.Parse(head[0], NumberStyles.Float, Inv) : 0.0;
            return whole + double.Parse(head[head.Length - 1], NumberStyles.Float, Inv) / den;
        }

        private static long Gcd(long a, long b)
        {
            while (b != 0)
            {
                var t = a % b;
                a = b;
                b = t;
            }
            return a;
        }
    }
}
