namespace ActualChat.UI.Blazor.Services;

public partial class AppActivity
{
    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.5, 3);
        return AsyncChain.From(PushState)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    private async Task PushState(CancellationToken cancellationToken)
    {
        var cGetState = await Computed
            .Capture(() => ComputeState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cGetState.Changes(FixedDelayer.Zero, cancellationToken);
        await foreach (var cState in changes.ConfigureAwait(false)) {
            var state = cState.Value;
            if (_state.Value == state)
                continue;

            Log.LogDebug("PushState: {OldState} -> {State}", _state.Value, state);
            _state.Value = state;
        }
    }

    [ComputeMethod]
    protected virtual async Task<ActivityState> ComputeState(CancellationToken cancellationToken)
    {
        var isBackground = await BackgroundStateTracker.IsBackground.Use(cancellationToken).ConfigureAwait(false);
        if (!isBackground)
            return ActivityState.Foreground;

        var isActiveInBackground = await MustBeBackgroundActive(cancellationToken).ConfigureAwait(false);
        return isActiveInBackground
            ? ActivityState.BackgroundActive
            : ActivityState.BackgroundIdle;
    }
}
