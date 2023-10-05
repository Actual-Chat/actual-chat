namespace ActualChat.UI.Blazor.Services;

public partial class BackgroundUI
{
    private static readonly TimeSpan ChangeBufferDuration = TimeSpan.FromMilliseconds(500);

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChainExt.From(PushActivityState),
        };
        var retryDelays = RetryDelaySeq.Exp(0.5, 3);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    private async Task PushActivityState(CancellationToken cancellationToken)
    {
        var cGetState = await Computed
            .Capture(() => GetState(cancellationToken))
            .ConfigureAwait(false);

        var stateChanges = cGetState.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        var throttledChanges = stateChanges.Throttle(ChangeBufferDuration, cancellationToken);
        await foreach (var cState in throttledChanges) {
            var state = cState.Value;
            if (_state.Value == state)
                continue;

            _state.Value = state;
        }
    }

    [ComputeMethod]
    protected virtual async Task<BackgroundState> GetState(CancellationToken cancellationToken)
    {
        var isActive = await BackgroundActivityProvider.GetIsActive(cancellationToken).ConfigureAwait(false);
        var isBackground = await GetIsBackground(cancellationToken).ConfigureAwait(false);
        var state = (isActive, isBackground) switch {
            (true, true) => BackgroundState.BackgroundActive,
            (_, true) => BackgroundState.BackgroundIdle,
            (_, false) => BackgroundState.Foreground,
        };
        return state;
    }
}
