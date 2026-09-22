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

        var projection = new TranscriptStreamProjection(content, isTranslation);
        try {
            // ProcessStreamingState cancels this token when the entry switches streams (translation
            // turned off mid-transcript). A remote RpcStream enumerator ends only with the stream
            // unless the token reaches it, and this loop would then keep writing the old stream's text.
            await foreach (var transcript in rpcStream.ToTranscripts(cancellationToken).ConfigureAwait(false))
                if (projection.Next(transcript) is { } state)
                    _state.Value = state;
        }
        catch (Exception e) when (e.IsCancellationOf(cancellationToken)) {
            // ProcessStreamingState cancels us right before it publishes the next stream's state,
            // so a terminal state from here would race it and win with the old stream's text.
            return true;
        }

        // Normal completion — mark streaming as done.
        // Any other error propagates to the retry loop with the state intact.
        _state.Value = projection.Complete();
        return true;
    }
}
