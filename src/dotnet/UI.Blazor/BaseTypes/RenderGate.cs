namespace ActualChat.UI.Blazor;

// A backgrounded WebView stops acknowledging Blazor's render batches while .NET keeps producing
// them, and every batch produced in that window is one the host can drop - which desyncs the
// renderer permanently. Holding renders back until the app is visible keeps that window empty.
public sealed class RenderGate : WorkerBase
{
    // Elsewhere the render batch transport is in-process, so nothing can be lost and postponing
    // would only add latency.
    public const bool IsEnabledOnMauiApp = true;

    private readonly BackgroundStateTracker? _backgroundStateTracker;
    private readonly HashSet<ComponentBase> _postponed = new();
    private readonly Lock _lock = new();

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

        lock (_lock)
            _postponed.Add(renderer);
        return true;
    }

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
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

    // Private methods

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
