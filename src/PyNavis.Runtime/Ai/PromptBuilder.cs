using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PyNavis.Runtime.Ai
{
    /// <summary>One entry in the transcript. A user turn may carry the selection
    /// summary that was attached to it.</summary>
    public sealed class Turn
    {
        public string Role;          // "user" or "assistant"
        public string Text;
        /// <summary>The attach-selection payload sent with this turn, or null.</summary>
        public string Selection;

        public Turn() { }

        public Turn(string role, string text)
        {
            Role = role;
            Text = text;
        }
    }

    /// <summary>The state behind one chat: its turns and the tool it is editing.</summary>
    public sealed class Conversation
    {
        public List<Turn> Turns { get; } = new List<Turn>();
        /// <summary>The bundle folder this conversation is revising, once created.</summary>
        public string CurrentBundleDir;
        /// <summary>The current tool's files, put in front of the model on every
        /// revision so it edits what is on disk rather than what it remembers.</summary>
        public Dictionary<string, string> CurrentFiles;
    }

    /// <summary>
    /// Assembles the request. The system prompt is the authoring pack plus the output
    /// rules, identical every turn so the provider can cache it. The conversation is
    /// sent as-is, oldest turns dropped in pairs when it outgrows the budget, with the
    /// current tool's files and any attached selection appended to the newest user turn.
    /// </summary>
    public static class PromptBuilder
    {
        /// <summary>Roughly 25k tokens of transcript before trimming starts.</summary>
        public const int MessageBudgetChars = 100_000;

        public const string OutputRules =
@"# How to answer in this chat

You are the assistant inside pyNavis, writing ribbon tools for Autodesk Navisworks. The
person asks for a tool in plain words; you answer with the files of ONE bundle, which
pyNavis writes to disk and puts on the ribbon when they click Create.

Answer in this exact shape:

1. One or two sentences saying what the tool does and how it reports its result.
2. A line `Title: <Tool Name>` (two or three words, Title Case, this is the button caption).
3. A line `Icon: <letters> <colour>` with one or two letters and one of: violet, teal,
   amber, rose, green.
4. A fenced block per file, the file name in the fence info string:

```yaml bundle.yaml
title: <Tool Name>
tooltip: <one sentence>
```

```python script.py
...
```

Only three files exist: bundle.yaml, script.py and, only when the tool has settings the
person may change, config.py (Shift+Click runs it). Never propose other files, folders,
or paths. Never write `if __name__ == '__main__':` (it never runs in pyNavis). Top-level
code only. Every outcome ends in a toast, never print(). Cancelling a dialog is silent.

If the request is unclear or needs a property or category name you cannot know, ask one
short question instead of guessing, and send no files. When the person attaches their
selection you get its category and property names only, never values; use exactly those
names and do not ask for values.

When the person sends the current files and asks for a change, reply with the complete
revised files, not a diff, and keep the same Title unless asked to rename it.

Use no em dashes anywhere. Keep the prose short; the code is the answer.

# The pyNavis authoring guide
";

        public static ChatRequest Build(string authoringPack, Conversation chat, AiSettings settings)
        {
            if (chat == null) throw new ArgumentNullException(nameof(chat));
            settings = settings ?? new AiSettings();

            var request = new ChatRequest
            {
                System = OutputRules + (authoringPack ?? ""),
                Model = settings.EffectiveModel,
                MaxTokens = settings.MaxTokens > 0 ? settings.MaxTokens : AiSettings.DefaultMaxTokens,
            };

            var turns = chat.Turns.Where(t => !string.IsNullOrEmpty(t.Text) || !string.IsNullOrEmpty(t.Selection)).ToList();
            var newestUser = turns.LastOrDefault(t => t.Role == "user");

            var messages = turns.Select(t => new ChatMessage(t.Role, Render(t, t == newestUser ? chat.CurrentFiles : null)))
                                .ToList();

            // Drop the oldest exchanges first, but always keep the newest user turn.
            while (messages.Count > 1 && messages.Sum(m => m.Content.Length) > MessageBudgetChars)
            {
                messages.RemoveAt(0);
                if (messages.Count > 1 && messages[0].Role == "assistant") messages.RemoveAt(0);
            }
            foreach (var m in messages) request.Messages.Add(m);
            return request;
        }

        private static string Render(Turn turn, Dictionary<string, string> currentFiles)
        {
            var sb = new StringBuilder(turn.Text ?? "");
            if (!string.IsNullOrEmpty(turn.Selection))
            {
                sb.Append("\n\nCurrent selection in Navisworks (attached by the user):\n```\n")
                  .Append(turn.Selection.TrimEnd()).Append("\n```");
            }
            if (currentFiles != null && currentFiles.Count > 0)
            {
                sb.Append("\n\nThe tool's current files, as they are on disk:\n");
                foreach (var name in Proposal.AllowedFiles)
                {
                    if (!currentFiles.TryGetValue(name, out var content)) continue;
                    var lang = name.EndsWith(".yaml") ? "yaml" : "python";
                    var body = (content ?? "").Replace("\r\n", "\n");
                    if (!body.EndsWith("\n")) body += "\n";
                    sb.Append("\n```").Append(lang).Append(' ').Append(name).Append('\n').Append(body).Append("```\n");
                }
            }
            return sb.ToString();
        }
    }
}
