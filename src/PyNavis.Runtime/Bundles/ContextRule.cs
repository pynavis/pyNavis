using System;
using System.Collections.Generic;

namespace PyNavis.Runtime.Bundles
{
    /// <summary>
    /// A bundle.yaml "context:" expression, e.g. "selection & clash-tests".
    /// Parsed once at bundle load; evaluated against a condition snapshot on every
    /// hub event. Pure: conditions are answered by the callback, never queried here.
    /// Grammar: or := and ('|' and)*; and := unary ('&' unary)*; unary := '!' unary | name.
    /// </summary>
    public class ContextRule
    {
        public static readonly string[] KnownConditions =
        {
            "doc", "selection", "clash-tests", "clash-results",
            "viewpoints", "selection-sets", "multi-model",
        };

        public string Text { get; }
        private readonly Node _root;

        private ContextRule(Node root, string text) { _root = root; Text = text; }

        public static ContextRule Parse(string text)
        {
            var tokens = Tokenize(text ?? "");
            if (tokens.Count == 0) throw new FormatException("Empty context rule.");
            var pos = 0;
            var root = ParseOr(tokens, ref pos, text);
            if (pos != tokens.Count)
                throw new FormatException($"Unexpected '{tokens[pos]}' in context rule '{text}'.");
            return new ContextRule(root, text);
        }

        public bool Evaluate(Func<string, bool> condition) => _root.Eval(condition);

        private static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '&' || c == '|' || c == '!') { tokens.Add(c.ToString()); i++; continue; }
                if (char.IsLetter(c))
                {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '-')) i++;
                    tokens.Add(text.Substring(start, i - start).ToLowerInvariant());
                    continue;
                }
                throw new FormatException($"Unexpected character '{c}' in context rule '{text}'.");
            }
            return tokens;
        }

        private static Node ParseOr(List<string> tokens, ref int pos, string text)
        {
            var left = ParseAnd(tokens, ref pos, text);
            while (pos < tokens.Count && tokens[pos] == "|")
            {
                pos++;
                left = new Or { L = left, R = ParseAnd(tokens, ref pos, text) };
            }
            return left;
        }

        private static Node ParseAnd(List<string> tokens, ref int pos, string text)
        {
            var left = ParseUnary(tokens, ref pos, text);
            while (pos < tokens.Count && tokens[pos] == "&")
            {
                pos++;
                left = new And { L = left, R = ParseUnary(tokens, ref pos, text) };
            }
            return left;
        }

        private static Node ParseUnary(List<string> tokens, ref int pos, string text)
        {
            if (pos >= tokens.Count)
                throw new FormatException($"Context rule '{text}' ends unexpectedly.");
            if (tokens[pos] == "!")
            {
                pos++;
                return new Not { Inner = ParseUnary(tokens, ref pos, text) };
            }
            var token = tokens[pos];
            if (token == "&" || token == "|")
                throw new FormatException($"Unexpected '{token}' in context rule '{text}'.");
            pos++;
            return new Cond { Name = token };
        }

        private abstract class Node { public abstract bool Eval(Func<string, bool> c); }
        private class Cond : Node { public string Name; public override bool Eval(Func<string, bool> c) => c(Name); }
        private class Not : Node { public Node Inner; public override bool Eval(Func<string, bool> c) => !Inner.Eval(c); }
        private class And : Node { public Node L, R; public override bool Eval(Func<string, bool> c) => L.Eval(c) && R.Eval(c); }
        private class Or : Node { public Node L, R; public override bool Eval(Func<string, bool> c) => L.Eval(c) || R.Eval(c); }
    }
}
