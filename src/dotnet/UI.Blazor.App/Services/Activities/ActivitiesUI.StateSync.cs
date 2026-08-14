using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ActivitiesUI
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
        var changes = cGetState.Changes(cancellationToken);
        await foreach (var cState in changes.ConfigureAwait(false)) {
            var state = cState.Value;
            if (_state.Value == state)
                continue;

            Log.LogDebug("PushState: {OldState} -> {State}", _state.Value, state);
            _state.Value = state;
        }
    }

    [ComputeMethod]
    protected virtual async Task<AppActivityState> ComputeState(CancellationToken cancellationToken)
    {
        var isBackground = await IsBackground.Use(cancellationToken).ConfigureAwait(false);
        if (!isBackground)
            return AppActivityState.Foreground;

        var set = await GetActivitySet(cancellationToken).ConfigureAwait(false);
        var hasAudioActivity = await HasAudioActivity(cancellationToken).ConfigureAwait(false);
        var mustBeActive = await MustBeBackgroundActive(cancellationToken).ConfigureAwait(false);

        return !set.IsEmpty || hasAudioActivity || mustBeActive
            ? AppActivityState.BackgroundActive
            : AppActivityState.BackgroundIdle;
    }
}
