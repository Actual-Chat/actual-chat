using ActualChat.Streaming;
using ActualChat.Transcription;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class TranscriptStreamReader(ChatEntryId id, AppUIHub hub) : WorkerBase
{
    // 0.5s to 2s, doubling. The previous ladder reached 9s and 13s cumulative, so a stream that
    // became available a few seconds in was not noticed for another ten - which is what left an
    // ellipsis or a "Transcribing" badge standing over a message that had already been transcribed.
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(0.5, 2, multiplier: 2);

    private readonly MutableState<TranscriptStreamReaderState> _state
        = hub.StateFactory.NewMutable(TranscriptStreamReaderState.None);

    private TranscriptUI TranscriptUI => hub.TranscriptUI;
    private ILiveAudioStreams LiveAudioStreams => hub.LiveAudioStreams;
    private MomentClockSet Clocks => hub.Clocks;
    private ILogger Log => field ??= hub.LogFor(GetType());

    public IState<TranscriptStreamReaderState> State => _state;

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(ProcessStreamingState)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(3, 60), Log)
            .CycleForever()
            .RunIsolated(cancellationToken);

    private async Task ProcessStreamingState(CancellationToken cancellationToken)
    {
        // Log.LogInformation("ProcessStreamingState: {Id}", id);
        var cStreamingState0 = await Computed
            .Capture(() => TranscriptUI.GetStreamingState(id, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        TranscriptUI.StreamingState? last = null;
        CancellationTokenSource? lastCts = null;
        try {
            await foreach (var (state, _) in cStreamingState0.Changes(cancellationToken).ConfigureAwait(false)) {
                if (state == last)
                    continue;

                lastCts?.CancelAndDisposeSilently();
                var linkedCts = cancellationToken.CreateLinkedTokenSource();
                if (state != null) {
                    var (_, content, isTranslation) = state;
                    // Log.LogWarning(
                    //     "ProcessStreamingState: Reset state for {MessageId}, State = {State}, OldState = {OldState}, {Hash}, {OldHash}",
                    //     id, cState.Value, last1, cState.Value.GetHashCode(), last1?.GetHashCode() ?? 0);
                    _state.Value = new(
                        RetainedText: "",
                        ChangedText: "",
                        AnimatedText: "",
                        Tail: content, // Will be empty for non-translation entries
                        true,
                        isTranslation);
                    _ = BackgroundTask.Run(
                        () => ProcessTranscriptWithRetry(state, linkedCts.Token),
                        Log,
                        $"{nameof(ProcessTranscript)} failed",
                        linkedCts.Token);
                }
                else {
                    // No streaming
                    _state.Value = TranscriptStreamReaderState.None;
                }
                last = state;
                lastCts = linkedCts;
            }
        }
        finally {
            lastCts?.CancelAndDisposeSilently();
        }
    }

    private async Task ProcessTranscriptWithRetry(
        TranscriptUI.StreamingState streamingState,
        CancellationToken cancellationToken)
    {
        var retryIndex = 0;
        while (!cancellationToken.IsCancellationRequested)
            try {
                var isCompleted = await ProcessTranscript(streamingState, cancellationToken).ConfigureAwait(false);
                if (isCompleted)
                    return;

                // Not published yet (entry-creation race) or already expired; retry -
                // ProcessStreamingState cancels us once the entry leaves the streaming state
                await Clocks.SystemClock.Delay(RetryDelays[retryIndex++], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                var delay = RetryDelays[retryIndex++];
                Log.LogWarning(e, "StreamTranscript failed for {Id}, retrying in {Delay}s", id, delay);
                await Clocks.SystemClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
    }

    private async Task<bool> ProcessTranscript(
        TranscriptUI.StreamingState streamingState,
        CancellationToken cancellationToken)
    {
        var (streamId, content, isTranslation) = streamingState;
        var rpcStream = await LiveAudioStreams.GetTranscriptStream(hub.Session, streamId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (rpcStream is null)
            return false;

        var lastText = "";
        try {
            var transcripts = rpcStream.ToTranscripts();

            var stablePrefixLength = 0;
            var lastWordIndex = isTranslation
                ? content.LastIndexOf(' ') + 1
                : 0;

            await foreach (var transcript in transcripts.ConfigureAwait(false)) {
                var text = transcript.Text;
                var isStable = transcript.IsStable;

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

                _state.Value = new(
                    RetainedText: text[..retainedLength],
                    ChangedText: changedPart[..animatedStartIndex],
                    AnimatedText: changedPart[animatedStartIndex..],
                    Tail: tail,
                    true,
                    isTranslation);

                if (isStable)
                    stablePrefixLength = Math.Max(stablePrefixLength, text.Length);

                lastText = text;
            }
        }
        catch (Exception e) when (e.IsCancellationOf(cancellationToken)) {
            // ProcessStreamingState cancels us right before it publishes the next stream's state,
            // so a terminal state from here would race it and win with the old stream's text.
            return true;
        }

        // Normal completion — mark streaming as done.
        // Any other error propagates to the retry loop with the state intact.
        _state.Value = new(
            RetainedText: lastText,
            ChangedText: "",
            AnimatedText: "",
            Tail: "",
            false,
            isTranslation);
        return true;
    }

    private static int GetRetainedLength(string previous, string current, int stablePrefixLength)
    {
        // Uses knowledge of an immutable prefix (stablePrefixLength) to avoid re-comparing it.
        // Also handles fast-paths for pure appends/truncations within the unstable suffix.
        if (previous.Length == 0 || current.Length == 0)
            return 0;

        var baseLen = Math.Min(stablePrefixLength, Math.Min(previous.Length, current.Length));

        // Fast append: current = previous + delta (beyond baseLen)
        if (previous.Length <= current.Length) {
            var prevSuffix = previous.AsSpan(baseLen);
            var currSuffix = current.AsSpan(baseLen);
            if (currSuffix.StartsWith(prevSuffix))
                return previous.Length;
        }

        // Fast truncate: previous = current + removed tail (beyond baseLen)
        if (current.Length <= previous.Length) {
            var currSuffix = current.AsSpan(baseLen);
            var prevSuffix = previous.AsSpan(baseLen);
            if (prevSuffix.StartsWith(currSuffix))
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
