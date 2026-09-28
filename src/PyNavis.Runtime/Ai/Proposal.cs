using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PyNavis.Runtime.Ai
{
    /// <summary>One or two letters on a coloured rounded square: the look of every
    /// generated tool, so it is always distinguishable from a shipped one.</summary>
    public sealed class IconSpec
    {
        public const string DefaultColour = "violet";

        /// <summary>The accents a generated icon may use. Blue is deliberately absent;
        /// blue is the shipped icons' accent.</summary>
        public static readonly string[] Colours = { "violet", "teal", "amber", "rose", "green" };

        public string Letters { get; }
        public string Colour { get; }

        public IconSpec(string letters, string colour)
        {
            Letters = NormaliseLetters(letters);
            colour = (colour ?? "").Trim().ToLowerInvariant();
            Colour = Colours.Contains(colour) ? colour : DefaultColour;
        }

        private static string NormaliseLetters(string letters)
        {
            var kept = new string((letters ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (kept.Length == 0) return "?";
            return kept.Length > 2 ? kept.Substring(0, 2) : kept;
        }

        /// <summary>Initials of a two-word title, first two letters of a one-word one.</summary>
        public static string LettersFor(string title)
        {
            var words = (title ?? "").Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Any(char.IsLetterOrDigit)).ToList();
            if (words.Count == 0) return "?";
            if (words.Count == 1)
            {
                var w = new string(words[0].Where(char.IsLetterOrDigit).ToArray());
                return w.Substring(0, Math.Min(2, w.Length)).ToUpperInvariant();
            }
            return (First(words[0]) + First(words[1])).ToUpperInvariant();
        }

        private static string First(string word) =>
            word.Where(char.IsLetterOrDigit).Take(1).Select(c => c.ToString()).FirstOrDefault() ?? "";
    }

    /// <summary>What the model proposed: the files of one bundle plus its prose.</summary>
    public sealed class Proposal
    {
        public static readonly string[] AllowedFiles = { "bundle.yaml", "script.py", "config.py" };

        public string Title;
        public IconSpec Icon = new IconSpec("?", IconSpec.DefaultColour);
        public string Explanation = "";
        public Dictionary<string, string> Files { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Fence names the parser refused: anything not one of the three files.</summary>
        public List<string> RefusedFiles { get; } = new List<string>();

        /// <summary>Creatable: a script and a title.</summary>
        public bool IsComplete => Files.ContainsKey("script.py") && !string.IsNullOrWhiteSpace(Title);
    }

    /// <summary>
    /// Pulls a Proposal out of a reply. Files are fenced blocks named either in the
    /// fence's info string ("```python script.py") or on the line just before the
    /// fence ("**script.py**", "File: script.py", "## script.py"). Only bundle.yaml,
    /// script.py and config.py are files; every other name is refused and listed.
    /// </summary>
    public static class ProposalParser
    {
        private static readonly Regex FenceOpen = new Regex(@"^\s{0,3}(`{3,}|~{3,})\s*(.*?)\s*$");
        private static readonly Regex TitleLine = new Regex(@"^\s*\**\s*Title\s*:\s*\**\s*(.+?)\s*\**\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex IconLine = new Regex(@"^\s*\**\s*Icon\s*:\s*\**\s*(\S+)(?:\s+(\S+))?\s*\**\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex YamlTitle = new Regex(@"^\s*title\s*:\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        private static readonly Regex NameToken = new Regex(@"[\w.\-/\\]+\.(py|yaml)", RegexOptions.IgnoreCase);

        public static Proposal Parse(string reply)
        {
            var proposal = new Proposal();
            var lines = (reply ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var prose = new StringBuilder();
            string titleLine = null;
            IconSpec iconLine = null;
            var unnamed = new List<string>();

            for (var i = 0; i < lines.Length; i++)
            {
                var open = FenceOpen.Match(lines[i]);
                if (open.Success)
                {
                    var fence = open.Groups[1].Value;
                    var info = open.Groups[2].Value;
                    var body = new StringBuilder();
                    var j = i + 1;
                    for (; j < lines.Length; j++)
                    {
                        var close = FenceOpen.Match(lines[j]);
                        if (close.Success && close.Groups[1].Value[0] == fence[0]
                            && close.Groups[1].Value.Length >= fence.Length && close.Groups[2].Value.Length == 0)
                            break;
                        body.Append(lines[j]).Append('\n');
                    }
                    var name = NameFromInfo(info) ?? NameFromPreceding(lines, i, prose);
                    var content = body.ToString();
                    if (name == null) unnamed.Add(content);
                    else if (IsAllowed(name)) proposal.Files[Canonical(name)] = content;
                    else proposal.RefusedFiles.Add(name);
                    i = j;
                    continue;
                }

                var t = TitleLine.Match(lines[i]);
                if (t.Success) { titleLine = t.Groups[1].Value.Trim(); continue; }
                var ic = IconLine.Match(lines[i]);
                if (ic.Success)
                {
                    iconLine = new IconSpec(ic.Groups[1].Value, ic.Groups[2].Success ? ic.Groups[2].Value : null);
                    continue;
                }
                prose.AppendLine(lines[i]);
            }

            // One anonymous fence that reads as Python is the script; the model just
            // forgot to name it.
            if (!proposal.Files.ContainsKey("script.py") && unnamed.Count == 1 && LooksLikePython(unnamed[0]))
                proposal.Files["script.py"] = unnamed[0];

            proposal.Title = TitleFromYaml(proposal) ?? titleLine;
            proposal.Icon = iconLine != null
                ? (iconLine.Letters == "?" && proposal.Title != null
                    ? new IconSpec(IconSpec.LettersFor(proposal.Title), iconLine.Colour) : iconLine)
                : new IconSpec(IconSpec.LettersFor(proposal.Title), IconSpec.DefaultColour);
            proposal.Explanation = Regex.Replace(prose.ToString().Trim(), @"\n{3,}", "\n\n");
            return proposal;
        }

        private static string TitleFromYaml(Proposal p)
        {
            if (!p.Files.TryGetValue("bundle.yaml", out var yaml)) return null;
            var m = YamlTitle.Match(yaml);
            if (!m.Success) return null;
            var title = m.Groups[1].Value.Trim().Trim('"', '\'').Trim();
            return title.Length == 0 ? null : title;
        }

        private static string NameFromInfo(string info)
        {
            if (string.IsNullOrWhiteSpace(info)) return null;
            foreach (var token in info.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = NameToken.Match(token);
                if (m.Success) return m.Value;
            }
            return null;
        }

        /// <summary>Looks at the nearest non-blank line above the fence; when it names a
        /// file, takes that name and drops the line from the prose.</summary>
        private static string NameFromPreceding(string[] lines, int fenceIndex, StringBuilder prose)
        {
            for (var k = fenceIndex - 1; k >= Math.Max(0, fenceIndex - 2); k--)
            {
                var line = lines[k].Trim();
                if (line.Length == 0) continue;
                var m = NameToken.Match(line);
                if (!m.Success) return null;
                // Only a short label ("File: x", "**x**", "## x", "x:"), not a sentence.
                var stripped = Regex.Replace(line, @"^\W*(file\s*:)?\s*|\W+$", "", RegexOptions.IgnoreCase).Trim();
                if (!string.Equals(stripped, m.Value, StringComparison.OrdinalIgnoreCase)) return null;
                RemoveLastProseLine(prose, lines[k]);
                return m.Value;
            }
            return null;
        }

        private static void RemoveLastProseLine(StringBuilder prose, string line)
        {
            var text = prose.ToString();
            var idx = text.LastIndexOf(line + Environment.NewLine, StringComparison.Ordinal);
            if (idx < 0) idx = text.LastIndexOf(line + "\n", StringComparison.Ordinal);
            if (idx >= 0)
            {
                prose.Clear();
                prose.Append(text.Substring(0, idx));
                var rest = text.Substring(idx + line.Length).TrimStart('\r', '\n');
                prose.Append(rest);
            }
        }

        private static bool IsAllowed(string name) =>
            Proposal.AllowedFiles.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

        private static string Canonical(string name) =>
            Proposal.AllowedFiles.First(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

        private static bool LooksLikePython(string text) =>
            Regex.IsMatch(text, @"^\s*(from|import)\s+\w", RegexOptions.Multiline)
            || Regex.IsMatch(text, @"^\s*def\s+\w+\s*\(", RegexOptions.Multiline);
    }
}
