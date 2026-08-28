using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor;

/// <summary>
/// A component whose renders can be paced onto <see cref="RenderDelayer"/>'s grid instead of
/// happening as soon as its state changes.
/// </summary>
public interface IRenderSyncTarget
{
    // Typically false until the component has rendered once - a first render has nothing to sync
    // with and pacing it only delays what the reader is waiting for.
    bool MustSyncRender { get; }
}

// Paces IRenderSyncTarget renders onto a fixed grid: several live transcripts each updating at their
// own rate otherwise produce that many independent render batches per second, every one of them
// re-rendering and re-measuring the list that holds them.
//
// The same postpone-and-flush machinery serves MustPostponeBackgroundRenders. A backgrounded WebView
// stops acknowledging Blazor's render batches while .NET keeps producing them, and every batch
// produced in that window is one the host can drop - which desyncs the renderer permanently. Holding
// renders back until the app is visible keeps that window empty.
public sealed class RenderDelayer : UIWorkerBase<UIHub>
{
    public static readonly TimeSpan RenderPeriod = TimeSpan.FromMilliseconds(100);
    // A quarter period past the render tick it feeds: recomputes run while the browser paints what
    // the tick produced, and every state has settled well before the next tick reads it.
    public static readonly TimeSpan UpdatePhase = TimeSpan.FromMilliseconds(25);

    // Non-null exactly while the app is backgrounded: its existence is the gate, so nothing joins
    // a set already flushed. Dispatcher-only, like TryPostpone - hence no synchronization.
    private HashSet<ComponentBase>? _postponed;
    // The renders waiting for the next grid tick, or null when none are. Null is also what keeps an
    // idle app from scheduling anything: a flush exists only while someone is waiting for one.
    private HashSet<ComponentBase>? _paced;
    // Lets the components a flush is resuming through TryPostpone, which they re-enter via
    // NotifyStateHasChanged - without it they would rejoin _paced and never render.
    private bool _isFlushingPaced;

    public IUpdateDelayer UpdateDelayer { get; }
    public bool MustPostponeBackgroundRenders { get; }

    public RenderDelayer(UIHub hub, bool mustStart = true) : base(hub)
    {
        MustPostponeBackgroundRenders = HostInfo.HostKind.IsMauiApp();
        UpdateDelayer = new GridDelayer(RenderPeriod, UpdatePhase, FixedDelayer.Defaults.RetryDelays);
        if (mustStart)
            this.Start();
    }

    public bool TryPostpone(ComponentBase renderer)
    {
        // Runs inside ShouldRender for every component on every render, so it must never throw:
        // one exception here would stop the whole app rendering rather than one component.
        if (_postponed is { } postponed) {
            postponed.Add(renderer);
            return true;
        }

        if (_isFlushingPaced || renderer is not IRenderSyncTarget { MustSyncRender: true })
            return false;

        if (_paced is { } paced) {
            paced.Add(renderer);
            return true;
        }

        _paced = [renderer];
        _ = SchedulePacedFlush();
        return true;
    }

    // Protected methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        if (!MustPostponeBackgroundRenders)
            return Task.CompletedTask;

        return AsyncChain.From(TrackBackgroundState)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.1, 1), Log)
            .RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task SchedulePacedFlush()
    {
        try {
            await Task.Delay(GetGridDelay(RenderPeriod, TimeSpan.Zero), StopToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e.IsCancellationOf(StopToken)) {
            return;
        }

        await Dispatcher.InvokeSafeAsync(FlushPaced, Log).ConfigureAwait(false);
    }

    private void FlushPaced()
    {
        if (_paced is not { } paced)
            return;

        // Cleared before the flush, so anything postponed while it runs starts the next tick's set
        // rather than joining one that is already draining.
        _paced = null;
        _isFlushingPaced = true;
        try {
            Resume(paced);
        }
        finally {
            _isFlushingPaced = false;
        }
    }

    private async Task TrackBackgroundState(CancellationToken cancellationToken)
    {
        await Hub.WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        var backgroundStateTracker = Services.GetRequiredService<BackgroundStateTracker>();
        var isBackgroundState = backgroundStateTracker.IsBackground;
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
        Resume(postponed);
    }

    private void Resume(HashSet<ComponentBase> renderers)
    {
        // One component that throws must not strand the rest, and logging inside the loop would
        // turn a systemic failure into a log storm - so the first error is reported once, after.
        var errorCount = 0;
        Exception? firstError = null;
        foreach (var renderer in renderers) {
            try {
                renderer.NotifyStateHasChanged();
            }
            catch (Exception e) {
                errorCount++;
                firstError ??= e;
            }
        }

        if (firstError != null)
            Log.LogError(firstError, "Resume: {ErrorCount}/{Count} postponed render(s) failed",
                errorCount, renderers.Count);
    }

    private static TimeSpan GetGridDelay(TimeSpan period, TimeSpan phase)
    {
        // Pull-based rather than a running timer: nothing is scheduled while no one is waiting, so
        // an app with no live content on screen costs nothing at all.
        var periodMs = (long)period.TotalMilliseconds;
        var phaseMs = (long)phase.TotalMilliseconds;
        var delayMs = (phaseMs - Environment.TickCount64).PositiveModulo(periodMs);
        return TimeSpan.FromMilliseconds(delayMs == 0 ? periodMs : delayMs);
    }

    // Nested types

    /// <summary>
    /// An <see cref="IUpdateDelayer"/> that releases on <see cref="RenderDelayer"/>'s grid rather
    /// than a fixed delay from the invalidation, so states feeding one tick settle together.
    /// </summary>
    private sealed record GridDelayer(TimeSpan Period, TimeSpan Phase, RetryDelaySeq RetryDelays)
        : IUpdateDelayer
    {
        public Task Delay(int retryCount, CancellationToken cancellationToken = default)
            => retryCount > 0
                ? Task.Delay(RetryDelays[retryCount], cancellationToken)
                : Task.Delay(GetGridDelay(Period, Phase), cancellationToken);
    }
}
