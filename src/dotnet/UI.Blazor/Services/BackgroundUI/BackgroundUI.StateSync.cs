﻿namespace ActualChat.UI.Blazor.Services;

public partial class BackgroundUI
{
    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.5, 3);
        return AsyncChainExt.From(PushState)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    private async Task PushState(CancellationToken cancellationToken)
    {
        var cGetState = await Computed
            .Capture(() => GetState(cancellationToken))
            .ConfigureAwait(false);

        var stateChanges = cGetState.Changes(FixedDelayer.Instant, cancellationToken);
        await foreach (var cState in stateChanges.ConfigureAwait(false)) {
            var state = cState.Value;
            if (_state.Value == state)
                continue;

            Log.LogDebug("PushState: {OldState} -> {State}", _state.Value, state);
            _state.Value = state;
        }
    }

    [ComputeMethod]
    protected virtual async Task<BackgroundState> GetState(CancellationToken cancellationToken)
    {
        var isBackground = await IsBackground(cancellationToken).ConfigureAwait(false);
        if (!isBackground)
            return BackgroundState.Foreground;

        var isActiveInBackground = await BackgroundActivities.IsActiveInBackground(cancellationToken).ConfigureAwait(false);
        return isActiveInBackground
            ? BackgroundState.BackgroundActive
            : BackgroundState.BackgroundIdle;
    }
}
