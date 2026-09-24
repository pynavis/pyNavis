using System;
using System.Globalization;
using System.Text;

namespace PyNavis.Runtime.Output.Html
{
    /// <summary>
    /// Builds everything the HTML output window feeds to WebView2: the page skeleton
    /// (CSS + the pynavisAppend JS entry point) and the per-append scripts. Pure
    /// string logic - fully unit-tested; the window is a thin shell over this.
    /// </summary>
    public static class HtmlPage
    {
        public static string Skeleton(OutputPalette palette, string accentHex, bool glassBody = false)
        {
            // glassBody is retained for call-site compatibility but ignored: the
            // design system v2 uses solid surfaces, never acrylic on content.
            var dark = ThemeMath.IsDarkColor(palette.BackgroundR, palette.BackgroundG, palette.BackgroundB);
            // Derived neutrals: OutputPalette.Accent is the border gray; muted text
            // is a fixed mid-gray per theme; surface is Chrome.
            var muted = dark ? "#9A9A9A" : "#6B6B6B";
            var line = Hex(palette.AccentR, palette.AccentG, palette.AccentB);
            var frame = dark ? "#6E6E6E" : "#A6A6A6";
            var rowHover = dark ? "#2B2B2B" : "#F7F7F7";

            var css = @"
:root {
  --bg: " + Hex(palette.BackgroundR, palette.BackgroundG, palette.BackgroundB) + @";
  --fg: " + Hex(palette.ForegroundR, palette.ForegroundG, palette.ForegroundB) + @";
  --err: " + Hex(palette.ErrorR, palette.ErrorG, palette.ErrorB) + @";
  --surface: " + Hex(palette.ChromeR, palette.ChromeG, palette.ChromeB) + @";
  --line: " + line + @";
  --frame: " + frame + @";
  --muted: " + muted + @";
  --row-hover: " + rowHover + @";
  --accent: " + accentHex + @";
  --err-wash: " + (dark ? "#3B2A2B" : "#FDF3F4") + @";
  --err-line: " + (dark ? "#6E3E41" : "#E8C4C8") + @";
  --warn-wash: " + (dark ? "#3F3A1F" : "#FEF9E6") + @";
  --warn-line: " + (dark ? "#8B7E42" : "#DEC88F") + @";
  /* Warning TEXT, distinct from --warn-line (a border tint, ~1.4:1 as text).
     Amber, WCAG AA on both the plain background and the warn wash:
     light #8A6A00 (relative luminance 0.157) on #FFFFFF is 5.1:1 and on
     --warn-wash #FEF9E6 (0.945) is 4.8:1; dark #E5C15D (0.556) on #1E1E1E
     (0.013) is 9.6:1 and on --warn-wash #3F3A1F (0.042) is 6.6:1. */
  --warn: " + (dark ? "#E5C15D" : "#8A6A00") + @";
  /* Chart series palette: chart-1 mirrors the theme accent; the other five
     are muted categorical hues (blue-gray, teal, ochre, plum, slate), each
     clearing 3:1 contrast against this theme's background.
     Dark set:  #8FA3B8 blue-gray, #5FB3A8 teal, #C9A227 ochre, #B084B5 plum, #8B96A6 slate.
     Light set: #4A6478 blue-gray, #2E7D74 teal, #8A6D1F ochre, #7A4F7D plum, #4C5666 slate. */
  --chart-1: var(--accent);
  --chart-2: " + (dark ? "#8FA3B8" : "#4A6478") + @";
  --chart-3: " + (dark ? "#5FB3A8" : "#2E7D74") + @";
  --chart-4: " + (dark ? "#C9A227" : "#8A6D1F") + @";
  --chart-5: " + (dark ? "#B084B5" : "#7A4F7D") + @";
  --chart-6: " + (dark ? "#8B96A6" : "#4C5666") + @";
}
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; color: var(--fg); background: var(--bg); }
body {
  font-family: 'Segoe UI Variable Text', 'Segoe UI', sans-serif;
  font-size: 13px; line-height: 1.55; padding: 16px 18px 26px;
  min-height: 100vh;
}
::-webkit-scrollbar { width: 10px; height: 10px; }
::-webkit-scrollbar-thumb {
  background: " + (dark ? "#4d4d4d" : "#c9c9c9") + @"; border-radius: 5px;
  border: 3px solid var(--bg); background-clip: padding-box;
}
::-webkit-scrollbar-thumb:hover { background: var(--muted); background-clip: padding-box; }
/* Keyboard focus is visible everywhere, matching the 2px accent ring on the desktop side. */
:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; border-radius: 4px; }
/* .pynavis-card is a plain spacing wrapper - the content inside carries any frame */
.pynavis-card { margin: 0 0 4px; }
.pynavis-card.err {
  border-left: 3px solid var(--err); background: var(--err-wash);
  border: 1px solid var(--err-line); border-left: 3px solid var(--err);
  border-radius: 4px; padding: 6px 12px; margin: 4px 0;
}
/* The -line vars are border tints and stay on the 3px rail; the text takes the
   full-strength token, which clears 4.5:1 on its own wash (--err on --err-wash
   is 6.0:1 light / 5.3:1 dark; see --warn's note for the amber arithmetic). */
.pynavis-warn {
  color: var(--warn); background: var(--warn-wash);
  border-left: 3px solid var(--warn-line);
  padding: 4px 12px; margin: 4px 0; border-radius: 0 4px 4px 0;
}
.pynavis-error {
  color: var(--err); background: var(--err-wash);
  border-left: 3px solid var(--err-line);
  padding: 4px 12px; margin: 4px 0; border-radius: 0 4px 4px 0;
}
/* Errors also replay as <pre> in a saved transcript (a traceback is
   whitespace-significant), so give that form the monospace text treatment. */
pre.pynavis-warn, pre.pynavis-error {
  font-family: Consolas, monospace; font-size: 12.5px; line-height: 1.5;
  white-space: pre-wrap; word-break: break-word;
}
pre.pynavis-text {
  font-family: Consolas, monospace; font-size: 12.5px; line-height: 1.5;
  margin: 0; white-space: pre-wrap; word-break: break-word;
}
pre.pynavis-text.err { color: var(--err); }
h1, h2, h3 { font-weight: 600; margin: 10px 0 6px; color: var(--fg); }
h1 { font-size: 20px; }
h2 { font-size: 16px; }
h3 { font-size: 14px; color: var(--muted); }
p { margin: 4px 0; }
hr { border: none; border-top: 1px solid var(--line); margin: 10px 0; }
code {
  font-family: Consolas, monospace; font-size: 12px;
  background: var(--surface); border: 1px solid var(--line);
  border-radius: 3px; padding: 1px 5px;
}
pre.codeblock {
  font-family: Consolas, monospace; font-size: 12px; line-height: 1.5;
  background: var(--surface); border: 1px solid var(--line);
  border-radius: 4px; padding: 12px 14px; overflow-x: auto;
}
table.pynavis {
  border-collapse: separate; border-spacing: 0; margin: 8px 0 4px;
  border-radius: 4px; overflow: hidden; width: 100%;
  border: 1px solid var(--frame);
}
table.pynavis th {
  text-align: left; font-weight: 600; color: var(--muted);
  background: var(--surface);
  padding: 7px 14px; border-bottom: 1px solid var(--line);
}
table.pynavis td {
  padding: 6px 14px; border-bottom: 1px solid var(--line);
}
/* Numbers are right-aligned because they are numbers, not because of position. */
table.pynavis td.num, table.pynavis th.num {
  text-align: right; font-variant-numeric: tabular-nums; color: var(--muted);
}
table.pynavis tr:last-child td { border-bottom: none; }
table.pynavis tr:hover td { background: var(--row-hover); }
a.pynavis-el {
  color: var(--accent); text-decoration: none; cursor: pointer;
}
a.pynavis-el:hover { text-decoration: underline; }
svg.pynavis-chart { max-width: 100%; height: auto; }
.pynavis-code {
  font-family: Consolas, monospace; font-size: 12.5px; line-height: 1.5;
  border: 1px solid var(--frame); border-radius: 4px;
  padding: 12px 14px; margin: 4px 0; overflow-x: auto;
  background: var(--surface); white-space: pre;
}
figure.pynavis-img { max-width: 100%; margin: 8px 0 4px; }
figure.pynavis-img img { max-width: 100%; height: auto; display: block; border-radius: 4px; }
figure.pynavis-img figcaption { color: var(--muted); font-size: 12px; margin-top: 6px; }
#pynavis-progress {
  position: sticky; bottom: 10px;
  background: var(--surface);
  border: 1px solid var(--line); border-radius: 4px;
  padding: 10px 14px; margin-top: 12px;
  transition: opacity 120ms ease-out;
}
#pynavis-progress .track {
  height: 4px; background: var(--line); border-radius: 2px; overflow: hidden;
}
#pynavis-progress .bar {
  height: 100%; width: 0%;
  background: var(--accent);
  border-radius: 2px; transition: width 120ms ease-out;
}
#pynavis-progress .label {
  font-size: 12px; color: var(--muted); margin-top: 5px;
  font-variant-numeric: tabular-nums;
}
";
            var js = @"
function pynavisCard(cls) {
  var card = document.createElement('div');
  card.className = 'pynavis-card' + (cls ? ' ' + cls : '');
  document.body.appendChild(card);
  return card;
}
function pynavisAppend(p) {
  var body = document.body;
  var stick = (window.innerHeight + window.scrollY) >= (body.scrollHeight - 40);
  if (p.kind === 'text' || p.kind === 'error') {
    // consecutive same-kind text extends the current glass card's <pre>
    var err = p.kind === 'error';
    var card = body.lastElementChild;
    var want = 'pynavis-card' + (err ? ' err' : '');
    if (!card || card.className !== want || !card.querySelector('pre.pynavis-text')) {
      card = pynavisCard(err ? 'err' : '');
      var pre = document.createElement('pre');
      pre.className = 'pynavis-text' + (err ? ' err' : '');
      card.appendChild(pre);
    }
    card.querySelector('pre.pynavis-text').textContent += p.content;
  } else if (p.kind === 'html') {
    pynavisCard('').innerHTML = p.content;
  } else if (p.kind === 'progress') {
    var box = document.getElementById('pynavis-progress');
    if (!box) {
      box = document.createElement('div');
      box.id = 'pynavis-progress';
      box.innerHTML = '<div class=""track""><div class=""bar""></div></div><div class=""label""></div>';
      body.appendChild(box);
    }
    box.querySelector('.bar').style.width = p.value + '%';
    box.querySelector('.label').textContent = p.content;
    if (p.value >= 100) setTimeout(function () { box.style.opacity = 0; setTimeout(function () { box.remove(); }, 350); }, 900);
  }
  if (stick) window.scrollTo(0, body.scrollHeight);
}
document.addEventListener('click', function (e) {
  var link = e.target.closest('a[data-pynavis-el]');
  if (!link) return;
  e.preventDefault();
  window.chrome.webview.postMessage({ select: parseInt(link.getAttribute('data-pynavis-el'), 10) });
});
";
            return "<!DOCTYPE html><html><head><meta charset='utf-8'><style>" + css
                 + "</style></head><body><script>" + js + "</script></body></html>";
        }

        public static string AppendScript(string kind, string content) =>
            "pynavisAppend({\"kind\":\"" + kind + "\",\"content\":\"" + JsonEscape(content) + "\"})";

        public static string ProgressScript(double fraction, string label)
        {
            var percent = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, fraction)) * 100);
            return "pynavisAppend({\"kind\":\"progress\",\"value\":"
                 + percent.ToString(CultureInfo.InvariantCulture)
                 + ",\"content\":\"" + JsonEscape(label ?? "") + "\"})";
        }

        /// <summary>
        /// A link that selects the item in the model. It is a control, so it
        /// carries href and tabindex: an anchor without them is skipped by Tab
        /// entirely, which made every element link mouse-only.
        /// </summary>
        public static string ElementLinkHtml(int index, string label) =>
            "<a class=\"pynavis-el\" href=\"#\" tabindex=\"0\" role=\"button\" data-pynavis-el=\""
            + index + "\">" + HtmlEscape(label) + "</a>";

        public static string HtmlEscape(string text) =>
            (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        public static string JsonEscape(string text)
        {
            var sb = new StringBuilder((text ?? "").Length + 8);
            foreach (var c in text ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '\u2028' || c == '\u2029')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string Hex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";
    }
}
