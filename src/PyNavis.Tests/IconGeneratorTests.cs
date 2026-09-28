using System;
using System.IO;
using System.Threading;
using PyNavis.Runtime.Ai;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The in-app icon painter for generated tools: a rounded square in one of the
    /// named accents, never the shipped blue, with one or two letters on it.
    /// </summary>
    public class IconGeneratorTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "pynavis_tests", Guid.NewGuid().ToString("N"));

        public IconGeneratorTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static void OnSta(Action body)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { body(); } catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static (int width, int height) PngSize(string path)
        {
            var head = new byte[24];
            using (var stream = File.OpenRead(path)) stream.Read(head, 0, head.Length);
            int Be(int o) => (head[o] << 24) | (head[o + 1] << 16) | (head[o + 2] << 8) | head[o + 3];
            return (Be(16), Be(20));
        }

        [Fact]
        public void WritesTheFourVariants_AtTheRibbonSizes()
        {
            OnSta(() =>
            {
                new IconGenerator().Write(_dir, new IconSpec("AB", "violet"));

                Assert.Equal((96, 96), PngSize(Path.Combine(_dir, "icon.png")));
                Assert.Equal((96, 96), PngSize(Path.Combine(_dir, "icon.dark.png")));
                Assert.Equal((32, 32), PngSize(Path.Combine(_dir, "icon.small.png")));
                Assert.Equal((32, 32), PngSize(Path.Combine(_dir, "icon.small.dark.png")));
            });
        }

        [Fact]
        public void ThePalette_HasNoBlue_SoGeneratedToolsNeverPassForShippedOnes()
        {
            Assert.Contains("violet", IconSpec.Colours);
            Assert.DoesNotContain("blue", IconSpec.Colours);
            Assert.Equal("violet", IconSpec.DefaultColour);
            foreach (var name in IconSpec.Colours)
            {
                var (light, dark) = IconGenerator.Accent(name);
                // Fluent blue is 0078D4 light, 4CC2FF dark; nothing here may be it.
                Assert.NotEqual((0x00, 0x78, 0xD4), (light.R, light.G, light.B));
                Assert.NotEqual((0x4C, 0xC2, 0xFF), (dark.R, dark.G, dark.B));
            }
        }

        [Fact]
        public void IconSpec_NormalisesLettersAndColour()
        {
            Assert.Equal("AB", new IconSpec("abc", "teal").Letters);
            Assert.Equal("teal", new IconSpec("abc", "TEAL").Colour);
            Assert.Equal(IconSpec.DefaultColour, new IconSpec("A", "blue").Colour);
            Assert.Equal("?", new IconSpec("", "violet").Letters);
            Assert.Equal("IL", IconSpec.LettersFor("Isolate Level"));
            Assert.Equal("PU", IconSpec.LettersFor("Purge"));
            Assert.Equal("?", IconSpec.LettersFor(""));
        }
    }
}
