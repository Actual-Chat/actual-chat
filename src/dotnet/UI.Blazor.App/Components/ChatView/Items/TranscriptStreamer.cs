using ActualChat.Streaming;
using ActualChat.Transcription;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class TranscriptStreamer(TextEntryId id, AppUIHub hub) : WorkerBase
{
    private readonly IMutableState<TranscriptStreamingState> _state = hub.StateFactory.NewMutable(TranscriptStreamingState.None);
    private TranscriptUI TranscriptUI => hub.TranscriptUI;
    private IStreamClient StreamClient => hub.StreamClient;
    public IState<TranscriptStreamingState> State => _state;
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
        var cGetState = await Computed.Capture(() => TranscriptUI.GetStreamingState(id, cancellationToken), cancellationToken).ConfigureAwait(false);
        TranscriptUI.StreamingState? last = null;
        CancellationTokenSource? lastCts = null;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var last1 = last;
                var ct = cancellationToken;
                var cState = await cGetState.When(s => s != last1, ct).ConfigureAwait(false);
                lastCts?.CancelAndDisposeSilently();
                var streamCts = cancellationToken.CreateLinkedTokenSource();
                if (cState.Value != null) {
                    var (_, entry, isTranslation) = cState.Value;
                    // Initial state
                    _state.Value = new (
                        RetainedText: "",
                        ChangedText: "",
                        AnimatedText: "",
                        Tail: entry.Content, // Will be empty for non-translation entries
                        true,
                        isTranslation);
                    _ = BackgroundTask.Run(
                        () => StreamTranscript(cState.Value, streamCts.Token),
                        Log,
                        $"{nameof(StreamTranscript)} failed",
                        streamCts.Token);
                }
                else
                    // No streaming
                    _state.Value = TranscriptStreamingState.None;
                last = cState.Value;
                lastCts = streamCts;
            }
        }
        finally {
            lastCts?.CancelAndDisposeSilently();
        }
    }

    private async Task StreamTranscript(TranscriptUI.StreamingState streamingState, CancellationToken cancellationToken) {
        try {
            var (streamId, entry, isTranslation) = streamingState;
            var diffs = StreamClient.GetTranscript(streamId.Value, cancellationToken);
            var transcripts = diffs
                .ToTranscripts()
                .Throttle(TimeSpan.FromMilliseconds(320), cancellationToken);
            var lastText = "";
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

    public sealed record TranscriptStreamingState(
        string RetainedText,
        string ChangedText,
        string AnimatedText,
        string Tail,
        bool IsStreaming,
        bool IsTranslating) {
        public static readonly TranscriptStreamingState None = new("", "", "", "", false, false);
    }
}
