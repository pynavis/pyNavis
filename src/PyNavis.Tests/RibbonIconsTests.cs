using System;
using System.IO;
using PyNavis.Runtime.Ribbon;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// AdWindows renders ribbon images at their natural WPF size (pixels scaled by
    /// the file's DPI metadata) - it does NOT fit them to the button slot. Icons
    /// must therefore load with their logical size pinned to the slot they fill
    /// (32 for large buttons, 16 for small rows and menu lists) while keeping the
    /// full pixel data for crisp high-DPI rendering.
    /// </summary>
    public class RibbonIconsTests
    {
        private static string ShippedIcon(string panel, string bundle, string file)
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var ext = Path.Combine(dir.FullName, "extensions", "pyNavis.extension");
                if (Directory.Exists(ext))
                    return Path.Combine(ext, "pyNavis.tab", panel, bundle, file);
            }
            throw new DirectoryNotFoundException("shipped pyNavis.extension not found above test dir");
        }

        [Fact]
        public void LargeIcon_LoadsAtLogical32_KeepingFullPixels()
        {
            var icon = RibbonIcons.Load(
                ShippedIcon("pyNavis.panel", "Console.pushbutton", "icon.png"), 32);

            Assert.Equal(32.0, icon.Width, 1);
            Assert.Equal(32.0, icon.Height, 1);
            Assert.Equal(96, icon.PixelWidth);
        }

        [Fact]
        public void SmallIcon_LoadsAtLogical16_KeepingFullPixels()
        {
            var icon = RibbonIcons.Load(
                ShippedIcon("pyNavis.panel", "Console.pushbutton", "icon.small.png"), 16);

            Assert.Equal(16.0, icon.Width, 1);
            Assert.Equal(16.0, icon.Height, 1);
            Assert.Equal(32, icon.PixelWidth);
        }

        [Fact]
        public void LargeIcon_FallingBackIntoSmallSlot_StillLandsAtLogical16()
        {
            // Bundles without icon.small.png reuse icon.png in the 16-slot.
            var icon = RibbonIcons.Load(
                ShippedIcon("pyNavis.panel", "Console.pushbutton", "icon.png"), 16);

            Assert.Equal(16.0, icon.Width, 1);
        }

        [Fact]
        public void LoadedIcon_IsFrozen_ForCrossThreadRibbonUse()
        {
            var icon = RibbonIcons.Load(
                ShippedIcon("pyNavis.panel", "Console.pushbutton", "icon.png"), 32);

            Assert.True(icon.IsFrozen);
        }

        [Fact]
        public void MissingFile_ReturnsNull_InsteadOfThrowing()
        {
            Assert.Null(RibbonIcons.Load(
                Path.Combine(Path.GetTempPath(), "pynavis-no-such-icon.png"), 32));
        }
    }
}
