using ActualChat.Streaming;
using ActualChat.Transcription;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class TranscriptStreamer(ChatEntryId id, AppUIHub hub) : WorkerBase
{
    private readonly IMutableState<StreamingState> _state = hub.StateFactory.NewMutable(StreamingState.None);
    private TranslationUI TranslationUI => hub.TranslationUI;
    private TranscriptUI TranscriptUI => hub.TranscriptUI;
    private IStreamClient StreamClient => hub.StreamClient;
    private Features Features => hub.Features;
    public IState<StreamingState> State => _state;
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.LogFor(GetType());

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(SyncStreamingState)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(3, 60))
            .CycleForever()
            .RunIsolated(cancellationToken);

    private async Task SyncStreamingState(CancellationToken cancellationToken)
    {
        var cGetState = await Computed.Capture(() => TranscriptUI.GetStreamingInput(id, cancellationToken), cancellationToken).ConfigureAwait(false);
        TranscriptUI.InputModel? last = null;
        while (!cancellationToken.IsCancellationRequested) {
            var last1 = last;
            var cState = await cGetState.When(s => s != last1, cancellationToken).ConfigureAwait(false);
            if (cState.Value?.MustStart(last) == true)
                await StreamTranscript(cState.Value.Entry, cancellationToken);
            last = cState.Value;
        }
    }

    private async Task StreamTranscript(
        ChatEntry entry,
        CancellationToken cancellationToken) {
        try {
            var isTranslation = await TranslationUI.MustTranslate(entry, true, cancellationToken).ConfigureAwait(false);
            if (isTranslation) {
                var isStreaming = await TranslationUI.IsStreaming(entry, cancellationToken);
                if (!isStreaming)
                    // Skip historical streaming if incomplete ui is disabled
                    return;
            }

            var diffs = isTranslation
                ? TranslationUI.GetTranscript(entry, cancellationToken)
                : StreamClient.GetTranscript(entry.StreamId, cancellationToken);
            var transcripts = diffs
                .ToTranscripts()
                .Throttle(TimeSpan.FromMilliseconds(320), cancellationToken);
            var lastText = "";
            if (isTranslation)
                // Initial state for translation
                _state.Value = new (
                    RetainedText: "",
                    ChangedText: "",
                    AnimatedText: "",
                    Tail: entry.Content,
                    true,
                    isTranslation);
            await foreach (var transcript in transcripts.ConfigureAwait(false)) {
                var text = transcript.Text;
                var retainedLength = lastText.GetCommonPrefixLength(text);
                var changedPart = text[retainedLength..];
                var animatedLength = (text.Length - lastText.Length).Clamp(0, changedPart.Length);
                var animatedStartIndex = changedPart.Length - animatedLength;
                var tail = "";
                if (isTranslation) {
                    var lastWordIndex = entry.Content.LastIndexOf(' ') + 1;
                    var tailStartIndex = text.Length.Clamp(0, lastWordIndex);
                    tail = entry.Content[tailStartIndex..];
                }
                _state.Value = new (
                    RetainedText: text[..retainedLength],
                    ChangedText: changedPart[..animatedStartIndex],
                    AnimatedText: changedPart[animatedStartIndex..],
                    Tail: tail,
                    true,
                    isTranslation);
                lastText = text;
            }

            _state.Value = new (
                    RetainedText: lastText,
                    ChangedText: "",
                    AnimatedText: "",
                    Tail: "",
                    false,
                    isTranslation);
        }
        catch (Exception e) {
            if (OrdinalEquals(e.GetType().FullName, "Microsoft.AspNetCore.SignalR.HubException")
                || !e.Message.OrdinalContains(nameof(OperationCanceledException)))
                throw;
            // Not fully sure if it's the case, but it seems that sometimes SignalR
            // wraps OperationCanceledException into HubException, so here we suppress it.
        }
    }

    public sealed record StreamingState(
        string RetainedText,
        string ChangedText,
        string AnimatedText,
        string Tail,
        bool IsStreaming,
        bool IsTranslating) {
        public static readonly StreamingState None = new("", "", "", "", false, false);
    }
}
