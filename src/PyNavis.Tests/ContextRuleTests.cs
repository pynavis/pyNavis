using System;
using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    public class ContextRuleTests
    {
        private static bool Eval(string text, params string[] truths)
        {
            var set = new System.Collections.Generic.HashSet<string>(truths, StringComparer.OrdinalIgnoreCase);
            return ContextRule.Parse(text).Evaluate(set.Contains);
        }

        [Fact]
        public void Single_Condition()
        {
            Assert.True(Eval("doc", "doc"));
            Assert.False(Eval("doc"));
        }

        [Fact]
        public void And_Requires_Both()
        {
            Assert.True(Eval("doc & selection", "doc", "selection"));
            Assert.False(Eval("doc & selection", "doc"));
        }

        [Fact]
        public void Or_Requires_Either()
        {
            Assert.True(Eval("clash-tests | viewpoints", "viewpoints"));
            Assert.False(Eval("clash-tests | viewpoints"));
        }

        [Fact]
        public void Not_Negates()
        {
            Assert.True(Eval("doc & !selection", "doc"));
            Assert.False(Eval("doc & !selection", "doc", "selection"));
        }

        [Fact]
        public void And_Binds_Tighter_Than_Or()
        {
            // a | b & c  ==  a | (b & c)
            Assert.True(Eval("doc | selection & multi-model", "doc"));
            Assert.False(Eval("doc | selection & multi-model", "selection"));
            Assert.True(Eval("doc | selection & multi-model", "selection", "multi-model"));
        }

        [Fact]
        public void Whitespace_And_Glued_Operators_Both_Parse()
        {
            Assert.True(Eval("doc&selection", "doc", "selection"));
            Assert.True(Eval("  doc  |  selection  ", "selection"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("doc &")]
        [InlineData("& doc")]
        [InlineData("doc doc")]
        [InlineData("doc ? selection")]
        public void Malformed_Throws(string text) =>
            Assert.Throws<FormatException>(() => ContextRule.Parse(text));

        [Fact]
        public void KnownConditions_Lists_The_Seven()
        {
            Assert.Equal(7, ContextRule.KnownConditions.Length);
            Assert.Contains("multi-model", ContextRule.KnownConditions);
        }
    }
}
