using PyNavis.Runtime.Bundles;
using Xunit;

namespace PyNavis.Tests
{
    public class ScriptMetadataTests
    {
        [Fact]
        public void Finds_DunderTitle_SingleAndDoubleQuotes()
        {
            Assert.Equal("My Tool", ScriptMetadata.Scan("__title__ = 'My Tool'\nprint(1)").Title);
            Assert.Equal("My Tool", ScriptMetadata.Scan("__title__ = \"My Tool\"").Title);
        }

        [Fact]
        public void Finds_ModuleDocstring_TripleDoubleQuotes()
        {
            var m = ScriptMetadata.Scan("\"\"\"Does a thing.\"\"\"\nimport sys\n");
            Assert.Equal("Does a thing.", m.Docstring);
        }

        [Fact]
        public void Finds_ModuleDocstring_TripleSingleQuotes()
        {
            var m = ScriptMetadata.Scan("'''Does a thing.'''\nimport sys\n");
            Assert.Equal("Does a thing.", m.Docstring);
        }

        [Fact]
        public void Finds_Docstring_AfterCommentsAndBlankLines()
        {
            var m = ScriptMetadata.Scan("# -*- coding: utf-8 -*-\n\n\"\"\"Tooltip here.\"\"\"\n");
            Assert.Equal("Tooltip here.", m.Docstring);
        }

        [Fact]
        public void Multiline_Docstring_IsPreserved()
        {
            var m = ScriptMetadata.Scan("\"\"\"Line one.\nLine two.\"\"\"\n");
            Assert.Equal("Line one.\nLine two.", m.Docstring);
        }

        [Fact]
        public void NoMetadata_YieldsNulls()
        {
            var m = ScriptMetadata.Scan("import sys\nprint(sys.version)\n");
            Assert.Null(m.Title);
            Assert.Null(m.Docstring);
        }

        [Fact]
        public void StringLiteral_AfterCode_IsNotADocstring()
        {
            var m = ScriptMetadata.Scan("import sys\n\"\"\"not a docstring\"\"\"\n");
            Assert.Null(m.Docstring);
        }
    }
}
