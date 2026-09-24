using System;
using System.IO;
using System.Text;
using PyNavis.Runtime;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// bundle.yaml and script.py are written by people in whatever their editor saves.
    /// File.ReadAllText assumes UTF-8, so a file saved as ANSI (Notepad's old default)
    /// came back with every accented character replaced by U+FFFD and no error: a
    /// mangled button title at best, a silently different string literal at worst.
    /// </summary>
    public class TextFilesTests : IDisposable
    {
        private readonly string _dir;

        public TextFilesTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string Write(byte[] bytes)
        {
            var path = Path.Combine(_dir, "bundle.yaml");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        [Fact]
        public void Utf8_WithoutBom_ReadsAsUtf8()
        {
            var path = Write(new UTF8Encoding(false).GetBytes("title: Größe"));
            Assert.Equal("title: Größe", TextFiles.Read(path));
        }

        [Fact]
        public void Utf8_WithBom_ReadsWithoutTheBom()
        {
            var path = Write(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("title: Größe")));
            Assert.Equal("title: Größe", TextFiles.Read(path));
        }

        [Fact]
        public void AnsiFile_IsNotMangled()
        {
            var ansi = Encoding.GetEncoding(1252);
            var path = Write(ansi.GetBytes("title: Größe"));

            Assert.Equal("title: Größe", TextFiles.Read(path, ansi));
        }

        [Fact]
        public void Utf16_WithBom_StillReads()
        {
            var path = Write(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("title: Größe")));
            Assert.Equal("title: Größe", TextFiles.Read(path));
        }
    }

    internal static class ByteArrayExtensions
    {
        public static byte[] Concat(this byte[] first, byte[] second)
        {
            var all = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, all, 0, first.Length);
            Buffer.BlockCopy(second, 0, all, first.Length, second.Length);
            return all;
        }
    }
}
