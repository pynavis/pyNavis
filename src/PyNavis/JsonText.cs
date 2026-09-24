using System.Globalization;
using System.Text;

namespace PyNavis
{
    /// <summary>
    /// Minimal JSON string-value unescaping for the loader's regex-based config read.
    /// Must handle everything PowerShell 5.1 ConvertTo-Json emits inside a string:
    /// backslash doubling, \/ \" \uXXXX and control escapes. (The loader deliberately
    /// has no JSON library; the runtime does full parsing separately.)
    /// </summary>
    internal static class JsonText
    {
        internal static string UnescapeString(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0) return value;

            var sb = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '\\' || i == value.Length - 1)
                {
                    sb.Append(c);
                    continue;
                }

                var next = value[++i];
                switch (next)
                {
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case '"': sb.Append('"'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 < value.Length && ushort.TryParse(
                                value.Substring(i + 1, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out var code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else
                        {
                            sb.Append('\\').Append(next);
                        }
                        break;
                    default:
                        sb.Append('\\').Append(next);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
