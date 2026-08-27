using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor;

// A backgrounded WebView stops acknowledging Blazor's render batches while .NET keeps producing
// them, and every batch produced in that window is one the host can drop - which desyncs the
// renderer permanently. Holding renders back until the app is visible keeps that window empty.
public sealed class RenderGate : UIWorkerBase<UIHub>
{
    // Elsewhere the render batch transport is in-process, so nothing can be lost and postponing
    // would only add latency.
    public const bool IsEnabledOnMauiApp = true;

    private readonly BackgroundStateTracker? _backgroundStateTracker;
    // Non-null exactly while the app is backgrounded: its existence is the gate, so nothing joins
    // a set already flushed. Dispatcher-only, like TryPostpone - hence no synchronization.
    private HashSet<ComponentBase>? _postponed;

    public bool IsEnabled => _backgroundStateTracker != null;

    public RenderGate(UIHub hub) : base(hub)
    {
        _backgroundStateTracker = IsEnabledOnMauiApp && HostInfo.HostKind.IsMauiApp()
            ? Services.GetService<BackgroundStateTracker>()
            : null;
        if (IsEnabled)
            this.Start();
    }

    public bool TryPostpone(ComponentBase renderer)
    {
        // Runs inside ShouldRender for every component on every render, so it must never throw:
        // one exception here would stop the whole app rendering rather than one component.
        if (_postponed is not { } postponed)
            return false;

        postponed.Add(renderer);
        return true;
    }

    // Protected methods

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(TrackBackgroundState)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.1, 1), Log)
            .RunIsolated(cancellationToken);

    // Private methods

    private async Task TrackBackgroundState(CancellationToken cancellationToken)
    {
        await Hub.WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        var isBackgroundState = _backgroundStateTracker!.IsBackground;
        var cIsBackground = await Computed
            .Capture(() => isBackgroundState.Use(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        // Every value is applied, not just transitions, so a retry re-syncs instead of awaiting an
        // edge it already missed; an error stops postponing, since a gate stuck shut freezes the UI.
        await foreach (var c in cIsBackground.Changes(cancellationToken).ConfigureAwait(false)) {
            var (isBackground, error) = c;
            var isPostponing = error == null && isBackground;
            await Dispatcher.InvokeSafeAsync(() => SetIsPostponing(isPostponing), Log).ConfigureAwait(false);
        }
    }

    private void SetIsPostponing(bool isPostponing)
    {
        if (isPostponing) {
            _postponed ??= [];
            return;
        }

        if (_postponed is not { } postponed)
            return;

        // Cleared before the flush, so a NotifyStateHasChanged that re-enters TryPostpone renders
        // inline instead of rejoining a set nothing will drain.
        _postponed = null;
        if (postponed.Count == 0)
            return;

        Log.LogInformation("Resuming {Count} postponed render(s)", postponed.Count);
        // One component that throws must not strand the rest, and logging inside the loop would
        // turn a systemic failure into a log storm - so the first error is reported once, after.
        var errorCount = 0;
        Exception? firstError = null;
        foreach (var renderer in postponed) {
            try {
                renderer.NotifyStateHasChanged();
            }
            catch (Exception e) {
                errorCount++;
                firstError ??= e;
            }
        }

        if (firstError != null)
            Log.LogError(firstError, "SetIsPostponing: {ErrorCount}/{Count} postponed render(s) failed",
                errorCount, postponed.Count);
    }
}
