using ActualChat.Streaming;
using ActualChat.Transcription;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class TranscriptStreamer(TextEntryId id, AppUIHub hub) : WorkerBase
{
    private static readonly TimeSpan TranscriptThrottleInterval = TimeSpan.FromMilliseconds(320); // LLM usually responds within this threshold
    private readonly IMutableState<TranscriptStreamerState> _state = hub.StateFactory.NewMutable(TranscriptStreamerState.None);
    private TranscriptUI TranscriptUI => hub.TranscriptUI;
    private IStreamClient StreamClient => hub.StreamClient;
    private MomentClockSet Clocks => hub.Clocks;
    private ILogger Log => field ??= hub.LogFor(GetType());

    public IState<TranscriptStreamerState> State => _state;

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(SyncStreamingState)
            .LogError(Log)
            .RetryForever(RetryDelaySeq.Exp(3, 60))
            .CycleForever()
            .RunIsolated(cancellationToken);

    private async Task SyncStreamingState(CancellationToken cancellationToken)
    {
        // Log.LogInformation("SyncStreamingState: {Id}", id);
        var cGetState = await Computed.Capture(() => TranscriptUI.GetStreamingState(id, cancellationToken), cancellationToken).ConfigureAwait(false);
        TranscriptUI.StreamingState? last = null;
        CancellationTokenSource? lastCts = null;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var last1 = last;
                var ct = cancellationToken;
                var cState = await cGetState.When(s => s != last1, ct).ConfigureAwait(false);
                if (cState.ValueOrDefault == last1)
                    continue; // Double-check

                lastCts?.CancelAndDisposeSilently();
                var streamCts = cancellationToken.CreateLinkedTokenSource();
                if (cState.Value != null) {
                    var (_, content, isTranslation) = cState.Value;
                    // Log.LogWarning("Reset state for {MessageId}, State = {State}, OldState = {OldState}, {Hash}, {OldHash}", id, cState.Value, last1, cState.Value.GetHashCode(), last1?.GetHashCode() ?? 0);
                    // Initial state
                    _state.Value = new (
                        RetainedText: "",
                        ChangedText: "",
                        AnimatedText: "",
                        Tail: content, // Will be empty for non-translation entries
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
                    _state.Value = TranscriptStreamerState.None;
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
            var (streamId, content, isTranslation) = streamingState;
            var diffs = StreamClient.GetTranscript(streamId.Value, cancellationToken);
            var transcripts = diffs
                .ToTranscripts()
                .ThrottleTranscript(TranscriptThrottleInterval, Clocks.SystemClock, cancellationToken);

            // Optimization state:
            // - stablePrefixLength: length of text that is known to be immutable (based on IsStable snapshots)
            var stablePrefixLength = 0;
            var lastText = "";
            // Precompute once for translation tail splitting
            var lastWordIndex = isTranslation
                ? content.LastIndexOf(' ') + 1
                : 0;

            await foreach (var transcript in transcripts.ConfigureAwait(false)) {
                var text = transcript.Text;
                var isStable = transcript.IsStable;

                // Fast retained prefix calc using stable prefix knowledge
                var retainedLength = GetRetainedLength(lastText, text, stablePrefixLength);

                var changedPart = text[retainedLength..];

                // Animate only the delta growth; if it shrinks or equal, no animation
                var animatedLength = (text.Length - lastText.Length).Clamp(0, changedPart.Length);
                var animatedStartIndex = changedPart.Length - animatedLength;

                var tail = "";
                if (isTranslation) {
                    var tailStartIndex = text.Length.Clamp(0, lastWordIndex);
                    tail = content[tailStartIndex..];
                }

                _state.Value = new (
                    RetainedText: text[..retainedLength],
                    ChangedText: changedPart[..animatedStartIndex],
                    AnimatedText: changedPart[animatedStartIndex..],
                    Tail: tail,
                    true,
                    isTranslation);

                // If current snapshot is stable, lock in the known-immutable prefix
                if (isStable)
                    stablePrefixLength = Math.Max(stablePrefixLength, text.Length);

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

    // Uses knowledge of an immutable prefix (stablePrefixLength) to avoid re-comparing it.
    // Also handles fast-paths for pure appends/truncations within the unstable suffix.
    private static int GetRetainedLength(string previous, string current, int stablePrefixLength)
    {
        if (previous.Length == 0 || current.Length == 0)
            return 0;

        var baseLen = Math.Min(stablePrefixLength, Math.Min(previous.Length, current.Length));

        // Fast append: current = previous + delta (beyond baseLen)
        if (previous.Length <= current.Length) {
            var prevSuffix = previous.AsSpan(baseLen);
            var currSuffix = current.AsSpan(baseLen);
            if (currSuffix.StartsWith(prevSuffix, StringComparison.Ordinal))
                return previous.Length;
        }

        // Fast truncate: previous = current + removed tail (beyond baseLen)
        if (current.Length <= previous.Length) {
            var currSuffix = current.AsSpan(baseLen);
            var prevSuffix = previous.AsSpan(baseLen);
            if (prevSuffix.StartsWith(currSuffix, StringComparison.Ordinal))
                return current.Length;
        }

        // Generic suffix common prefix scan after the stable base
        var a = previous.AsSpan(baseLen);
        var b = current.AsSpan(baseLen);
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[i] == b[i])
            i++;
        return baseLen + i;
    }
}

public sealed record TranscriptStreamerState(
    string RetainedText,
    string ChangedText,
    string AnimatedText,
    string Tail,
    bool IsStreaming,
    bool IsTranslating) {
    public static readonly TranscriptStreamerState None = new("", "", "", "", false, false);
}
