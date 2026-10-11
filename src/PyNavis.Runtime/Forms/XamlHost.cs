using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace PyNavis.Runtime.Forms
{
    /// <summary>Loads a bundle-shipped .xaml file into a Window (pynavis.forms.WPFWindow).</summary>
    public static class XamlHost
    {
        public static Window Load(string path)
        {
            var root = Read(path);
            if (root is Window window) return window;
            return new Window
            {
                Content = root,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
        }

        /// <summary>
        /// Parses a bundle's .xaml with the file's own folder as the base URI, so a
        /// relative reference such as Image Source="icon.png" resolves beside the file.
        /// Without one it resolves against the Navisworks entry assembly and fails.
        /// Shared by forms.WPFWindow and dock panes' pane.xaml.
        /// </summary>
        internal static object Read(string path)
        {
            var full = Path.GetFullPath(path);
            var folder = Path.GetDirectoryName(full);
            if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                folder += Path.DirectorySeparatorChar;
            var context = new ParserContext { BaseUri = new Uri(folder, UriKind.Absolute) };

            using (var stream = File.OpenRead(full))
                return XamlReader.Load(stream, context);
        }
    }
}
