using System.Text;

namespace PyNavis.Runtime.Panes
{
    /// <summary>
    /// C# source for extra pane slots. The in-box .NET Framework compiler that builds
    /// this is a C# 5 compiler, so the emitted text must stay C# 5: no interpolation,
    /// no expression-bodied members, no auto-property initializers.
    /// </summary>
    public static class PaneSatelliteSource
    {
        public static string Emit(int firstSlot, int lastSlot)
        {
            var text = new StringBuilder();
            text.AppendLine("using Autodesk.Navisworks.Api.Plugins;");
            text.AppendLine();
            text.AppendLine("namespace PyNavis.Panes");
            text.AppendLine("{");
            for (var slot = firstSlot; slot <= lastSlot; slot++)
            {
                text.AppendLine("    [Plugin(\"PyNavis.Pane" + slot + "\", \"PYNV\",");
                text.AppendLine("        DisplayName = \"pyNavis Panel " + slot + "\",");
                text.AppendLine("        ToolTip = \"pyNavis dock panel\")]");
                text.AppendLine("    [DockPanePlugin(320, 420, AutoScroll = false, " +
                                "MinimumWidth = 200, MinimumHeight = 160)]");
                text.AppendLine("    public class PaneSlot" + slot + " : PyNavis.PaneSlotBase");
                text.AppendLine("    {");
                text.AppendLine("        protected override int Slot { get { return " + slot + "; } }");
                text.AppendLine("    }");
                text.AppendLine();
            }
            text.AppendLine("}");
            return text.ToString();
        }
    }
}
