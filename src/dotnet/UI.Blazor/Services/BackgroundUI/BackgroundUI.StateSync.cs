namespace ActualChat.UI.Blazor.Services;

public partial class BackgroundUI
{
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

        var stateChanges = cGetState.Changes(FixedDelayer.Instant, cancellationToken);
        await foreach (var cState in stateChanges) {
            var state = cState.Value;
            Log.LogDebug("PushActivityState: {OldState} -> {State}", _state.Value, state);
            if (_state.Value == state)
                continue;

            _state.Value = state;
        }
    }

    [ComputeMethod]
    protected virtual async Task<BackgroundState> GetState(CancellationToken cancellationToken)
    {
        var isActive = await BackgroundActivities.IsActiveInBackground(cancellationToken).ConfigureAwait(false);
        var isBackground = await GetIsBackground(cancellationToken).ConfigureAwait(false);
        Log.LogDebug("GetState: {IsActive} {IsBackground}", isActive, isBackground);
        var state = (isActive, isBackground) switch {
            (true, true) => BackgroundState.BackgroundActive,
            (_, true) => BackgroundState.BackgroundIdle,
            (_, false) => BackgroundState.Foreground,
        };
        Log.LogDebug("GetState: {State}", state);
        return state;
    }
}
