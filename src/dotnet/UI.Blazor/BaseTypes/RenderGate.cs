namespace ActualChat.UI.Blazor;

// A backgrounded WebView stops acknowledging Blazor's render batches while .NET keeps producing
// them, and every batch produced in that window is one the host can drop - which desyncs the
// renderer permanently. Holding renders back until the app is visible keeps that window empty.
public sealed class RenderGate : WorkerBase
{
    // Elsewhere the render batch transport is in-process, so nothing can be lost and postponing
    // would only add latency.
    public const bool IsEnabledOnMauiApp = true;
    private static readonly TimeSpan WatchdogPeriod = TimeSpan.FromSeconds(2);
    // Long enough that a resume racing a render never trips it, short enough that a lost one
    // doesn't read as a frozen app.
    private static readonly TimeSpan MaxForegroundPostponeDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StuckReportPeriod = TimeSpan.FromMinutes(1);

    private readonly BackgroundStateTracker? _backgroundStateTracker;
    private readonly HashSet<ComponentBase> _postponed = new();
    private readonly Lock _lock = new();
    private CpuTimestamp _postponedAt;
    private CpuTimestamp _reportedStuckAt;

    private ILogger Log { get; }
    public bool IsEnabled => _backgroundStateTracker != null;

    public RenderGate(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        _backgroundStateTracker = IsEnabledOnMauiApp && services.HostInfo().HostKind.IsMauiApp()
            ? services.GetService<BackgroundStateTracker>()
            : null;
        if (IsEnabled)
            this.Start();
    }

    // Runs inside ShouldRender for every component on every render, so it must never throw:
    // one exception here would stop the whole app rendering rather than one component.
    public bool TryPostpone(ComponentBase renderer)
    {
        if (_backgroundStateTracker is not { } backgroundStateTracker)
            return false;

        var computed = backgroundStateTracker.IsBackground.Computed;
        if (computed.HasError || !computed.Value)
            return false;

        lock (_lock) {
            if (_postponed.Count == 0)
                _postponedAt = CpuTimestamp.Now;
            _postponed.Add(renderer);
        }

        return true;
    }

    // Protected methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(ResumeWhenForeground),
            AsyncChain.From(WatchPostponed),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return baseChains
            .Select(chain => chain.Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log))
            .RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task ResumeWhenForeground(CancellationToken cancellationToken)
    {
        var isBackgroundState = _backgroundStateTracker!.IsBackground;
        var cIsBackground = await Computed
            .Capture(() => isBackgroundState.Use(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var wasBackground = cIsBackground.Value;
        await foreach (var c in cIsBackground.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            var isBackground = c.Value;
            if (wasBackground && !isBackground)
                Resume();
            wasBackground = isBackground;
        }
    }

    private async Task WatchPostponed(CancellationToken cancellationToken)
    {
        // The gate has exactly one way out - a background -> foreground transition - so a resume
        // that never arrives leaves the UI frozen with nothing to say so. Resuming is only safe
        // while the app reports itself foreground; a genuinely backgrounded WebView still must not
        // be handed batches it would drop, so a stuck background flag is reported rather than
        // overridden.
        var isBackgroundState = _backgroundStateTracker!.IsBackground;
        while (true) {
            await Task.Delay(WatchdogPeriod, cancellationToken).ConfigureAwait(false);
            var computed = isBackgroundState.Computed;
            var isBackground = !computed.HasError && computed.Value;
            int count;
            TimeSpan duration;
            lock (_lock) {
                count = _postponed.Count;
                duration = count == 0 ? TimeSpan.Zero : _postponedAt.Elapsed;
            }

            if (count == 0)
                continue;

            if (!isBackground) {
                if (duration >= MaxForegroundPostponeDuration) {
                    Log.LogWarning("{Count} render(s) postponed for {Duration} while foreground - resuming",
                        count, duration.ToShortString());
                    Resume();
                }
                continue;
            }

            if (duration < StuckReportPeriod || _reportedStuckAt.Elapsed < StuckReportPeriod)
                continue;

            _reportedStuckAt = CpuTimestamp.Now;
            Log.LogWarning("{Count} render(s) postponed for {Duration} - the app still reports it is backgrounded",
                count, duration.ToShortString());
        }
    }

    private void Resume()
    {
        ComponentBase[] postponed;
        lock (_lock) {
            if (_postponed.Count == 0)
                return;

            postponed = _postponed.ToArray();
            _postponed.Clear();
        }
        Log.LogInformation("Resuming {Count} postponed render(s)", postponed.Length);
        foreach (var renderer in postponed)
            renderer.NotifyStateHasChanged();
    }
}
