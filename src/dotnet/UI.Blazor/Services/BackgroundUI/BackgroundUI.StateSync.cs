namespace ActualChat.UI.Blazor.Services;

public partial class BackgroundUI
{
    private static readonly TimeSpan ChangeBufferDuration = TimeSpan.FromMilliseconds(2000);

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        Log.LogWarning("BackgroundUI-RUN");
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
        Log.LogWarning("BackgroundUI->PushActivityState");
        var cGetIsActive = await Computed
            .Capture(() => BackgroundActivityProvider.GetIsActive(cancellationToken))
            .ConfigureAwait(false);

        var cGetIsBackground = await Computed
            .Capture(() => GetIsBackground(cancellationToken))
            .ConfigureAwait(false);

        var isActiveChanges = cGetIsActive
            .Changes(cancellationToken)
            .Select(ca => new ActivityState(ca.Value, null ));

        var isBackgroundChanges = cGetIsBackground
            .Changes(cancellationToken)
            .Select(cb => new ActivityState(null, cb.Value ));

        var mergedChanges = isActiveChanges.Merge(isBackgroundChanges);

        var current = new ActivityState();
        var bufferedChanges = mergedChanges
            .Buffer(ChangeBufferDuration, Clocks.CoarseSystemClock, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var changeWindow in bufferedChanges) {
            Log.LogWarning("BackgroundUI-Change {RawState}", changeWindow);
            current = changeWindow.Aggregate(
                current,
                (l, r) => new ActivityState(r.IsActive ?? l.IsActive, r.IsBackground ?? l.IsBackground));
            var state = current switch {
                (true, true) => BackgroundState.BackgroundActive,
                (_, true) => BackgroundState.BackgroundIdle,
                (_, false) => BackgroundState.Foreground,
                _ => BackgroundState.Foreground,
            };
            if (_state.Value == state) {
                Log.LogWarning("BACKGROUND_STATE IS THE SAME - {State}", state);
                continue;
            }

            _state.Value = state;
            Log.LogWarning("CALCULATED BACKGROUND_STATE - {State}", state);
        }
        Log.LogWarning("BackgroundUI<-PushActivityState");
    }

    private record struct ActivityState(bool? IsActive, bool? IsBackground);
}
