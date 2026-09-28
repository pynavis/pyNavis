using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PyNavis.Runtime.Ai
{
    /// <summary>What one exchange came back with.</summary>
    public sealed class TurnOutcome
    {
        public string Text = "";
        public Proposal Proposal;
        public ChatResult Result;
        /// <summary>The wire failure, worded for the transcript; null on success.</summary>
        public string Error;
        public bool IsAuthFailure;
        public bool Cancelled;
    }

    /// <summary>
    /// The conversation loop behind the Ask AI pane, with no UI in it so it can be
    /// driven by tests: keeps the transcript, sends it through the provider, parses
    /// the reply into a proposal, writes the proposal to disk on request, and puts
    /// the written files back into the conversation for the next revision.
    /// </summary>
    public sealed class AiSession
    {
        private readonly Func<IChatProvider> _provider;
        private readonly Func<AiSettings> _settings;
        private readonly string _pack;
        private readonly GeneratedExtensionWriter _writer;
        private readonly Func<string> _selectionSummary;
        private bool _applied;

        public Conversation Conversation { get; private set; } = new Conversation();
        public Proposal LastProposal { get; private set; }
        public bool IsBusy { get; private set; }

        /// <summary>Text deltas as they stream, on the caller's thread.</summary>
        public event Action<string> TextStreamed;

        public AiSession(Func<IChatProvider> provider, Func<AiSettings> settings, string authoringPack,
            GeneratedExtensionWriter writer, Func<string> selectionSummary)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _pack = authoringPack ?? "";
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _selectionSummary = selectionSummary ?? (() => null);
        }

        /// <summary>The live session: real provider from settings and the secret store,
        /// the shipped pack, the per-user AI.extension, the current Navisworks selection.</summary>
        public static AiSession Live() => new AiSession(
            () =>
            {
                var settings = Config.PyNavisConfig.Load(RuntimeHost.UserConfigPath).Ai;
                return ChatProviders.Create(settings, SecretStore.Default.Get(SecretStore.AiKeyName));
            },
            () => Config.PyNavisConfig.Load(RuntimeHost.UserConfigPath).Ai,
            AuthoringPack.Load(),
            GeneratedExtensionWriter.Default,
            SelectionReader.Summarise);

        public string CurrentBundleDir => Conversation.CurrentBundleDir;
        public bool IsRevising => CurrentBundleDir != null;
        public string CurrentTitle { get; private set; }

        /// <summary>A complete proposal has arrived since the last Apply.</summary>
        public bool CanApply => LastProposal != null && LastProposal.IsComplete && !_applied;

        /// <summary>The current tool's most recent traceback, or null.</summary>
        public string LastError => CurrentBundleDir == null ? null : FailureLog.LastFor(CurrentBundleDir);

        public GeneratedExtensionWriter Writer => _writer;

        /// <summary>The authoring pack this session prompts with; the Copy button hands it out.</summary>
        public string AuthoringPackText => _pack;

        /// <summary>The settings as they stand now, read through the same source the
        /// requests use.</summary>
        public AiSettings CurrentSettings
        {
            get { try { return _settings() ?? new AiSettings(); } catch { return new AiSettings(); } }
        }

        public async Task<TurnOutcome> SendAsync(string text, bool attachSelection, CancellationToken token)
        {
            if (IsBusy) throw new InvalidOperationException("A reply is still streaming.");
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Nothing to send.", nameof(text));

            var turn = new Turn("user", text.Trim());
            if (attachSelection)
            {
                try { turn.Selection = _selectionSummary(); }
                catch (Exception ex)
                {
                    Log.Error("Could not summarise the selection", ex);
                    turn.Selection = "(the selection could not be read: " + ex.Message + ")";
                }
            }
            Conversation.Turns.Add(turn);
            return await RunAsync(token).ConfigureAwait(false);
        }

        /// <summary>Sends the current tool's last traceback as the next user turn.</summary>
        public async Task<TurnOutcome> SendLastErrorAsync(CancellationToken token)
        {
            var error = LastError;
            if (error == null) throw new InvalidOperationException("There is no recorded error for this tool.");
            FailureLog.Clear(CurrentBundleDir);
            Conversation.Turns.Add(new Turn("user",
                "The tool failed when I ran it. Fix it and send the complete files again.\n\n```\n" + error + "\n```"));
            return await RunAsync(token).ConfigureAwait(false);
        }

        private async Task<TurnOutcome> RunAsync(CancellationToken token)
        {
            IsBusy = true;
            var outcome = new TurnOutcome();
            var sb = new StringBuilder();
            try
            {
                var request = PromptBuilder.Build(_pack, Conversation, _settings());
                var provider = _provider();
                outcome.Result = await provider.StreamAsync(request, delta =>
                {
                    sb.Append(delta);
                    TextStreamed?.Invoke(delta);
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                outcome.Cancelled = true;
            }
            catch (ProviderException ex)
            {
                outcome.Error = ex.Message;
                outcome.IsAuthFailure = ex.IsAuthFailure;
                Log.Error("AI request failed: " + ex.Message);
                return outcome;
            }
            catch (Exception ex)
            {
                outcome.Error = "The request failed: " + ex.Message;
                Log.Error("AI request failed", ex);
                return outcome;
            }
            finally
            {
                IsBusy = false;
            }

            outcome.Text = sb.ToString();
            if (outcome.Cancelled)
            {
                if (outcome.Text.Length > 0)
                    Conversation.Turns.Add(new Turn("assistant", outcome.Text + "\n\n(cut short: you cancelled)"));
                else
                    Conversation.Turns.RemoveAt(Conversation.Turns.Count - 1);   // nothing came back: drop the ask
                return outcome;
            }

            Conversation.Turns.Add(new Turn("assistant", outcome.Text));
            outcome.Proposal = ProposalParser.Parse(outcome.Text);
            if (outcome.Proposal.IsComplete)
            {
                LastProposal = outcome.Proposal;
                _applied = false;
            }
            return outcome;
        }

        /// <summary>Writes the last proposal: a new bundle the first time, the same
        /// folder afterwards. Returns the bundle folder.</summary>
        public string Apply()
        {
            if (!CanApply) throw new InvalidOperationException("There is no new proposal to write.");
            string bundle;
            if (IsRevising)
            {
                _writer.Update(CurrentBundleDir, LastProposal);
                bundle = CurrentBundleDir;
            }
            else
            {
                bundle = _writer.Create(LastProposal);
                Conversation.CurrentBundleDir = bundle;
            }
            Conversation.CurrentFiles = _writer.ReadFiles(bundle);
            CurrentTitle = LastProposal.Title;
            FailureLog.Clear(bundle);
            _applied = true;
            return bundle;
        }

        /// <summary>Continue an existing generated tool in a fresh conversation.</summary>
        public void OpenTool(GeneratedTool tool)
        {
            Reset();
            Conversation.CurrentBundleDir = tool.Directory;
            Conversation.CurrentFiles = _writer.ReadFiles(tool.Directory);
            CurrentTitle = tool.Title;
        }

        public void Reset()
        {
            Conversation = new Conversation();
            LastProposal = null;
            CurrentTitle = null;
            _applied = false;
        }
    }
}
