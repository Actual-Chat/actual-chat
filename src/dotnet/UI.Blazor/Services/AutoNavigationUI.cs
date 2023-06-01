using Stl.Diagnostics;

namespace ActualChat.UI.Blazor.Services;

public enum AutoNavigationReason
{
    Skip = 0,
    Initial = 1,
    SignIn = 2,
    Other = 9,
    Notification = 10,
}

public abstract class AutoNavigationUI : IHasServices
{
    private readonly object _lock = new ();
    private bool _autoNavigateStarted;
    private readonly List<(LocalUrl Url, AutoNavigationReason Reason)> _initialNavigationTargets = new();

    private History? _history;
    private AppBlazorCircuitContext? _blazorCircuitContext;

    protected ILogger Log { get; }
    protected ILogger? DebugLog { get; }

    public IServiceProvider Services { get; }
    public History History => _history ??= Services.GetRequiredService<History>();
    public AppBlazorCircuitContext BlazorCircuitContext => _blazorCircuitContext ??= Services.GetRequiredService<AppBlazorCircuitContext>();
    public Dispatcher Dispatcher => BlazorCircuitContext.Dispatcher;

    public Task? WhenAutoNavigated { get; private set; }
    public bool? InitialLeftPanelIsVisible { get; set; }

    protected AutoNavigationUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        DebugLog = Log.IfEnabled(LogLevel.Debug);
    }

    public Task AutoNavigate(CancellationToken cancellationToken)
        => WhenAutoNavigated ??= Dispatcher.InvokeAsync((Func<Task>)(() => {
            LocalUrl url;
            AutoNavigationReason reason;
            lock (_lock) {
                _autoNavigateStarted = true;
                (url, reason) = _initialNavigationTargets.Count > 0
                    ? _initialNavigationTargets.MaxBy(t => (int) t.Reason)
                    : default;
                Log.LogDebug("AutoNavigate: Targets.Count: {Count}, Url: {Url}, Reason: '{Reason}'",
                    _initialNavigationTargets.Count, url, reason);
            }
            return HandleAutoNavigate(url, reason, cancellationToken);
        }));

    public void DispatchNavigateTo(LocalUrl url, AutoNavigationReason reason)
    {
        DebugLog?.LogDebug("DispatchNavigateTo({Url}, {Reason})", url, reason);
        lock (_lock) {
            if (!_autoNavigateStarted) {
                DebugLog?.LogDebug("DispatchNavigateTo({Url}, {Reason}): enqueuing initial navigation target", url, reason);
                _initialNavigationTargets.Add((url, reason));
                return;
            }
        }
        if (BlazorCircuitContext.WhenReady.IsCompleted) {
            if (Dispatcher.CheckAccess())
                NavigateTo(url, reason);
            else
                Dispatcher.InvokeAsync(() => NavigateTo(url, reason));
            return;
        }
        BlazorCircuitContext.WhenReady.ContinueWith(_ => {
            if (Dispatcher.CheckAccess())
                NavigateTo(url, reason);
            else
                Dispatcher.InvokeAsync(() => NavigateTo(url, reason));
        }, TaskScheduler.Current);
    }

    public void NavigateTo(LocalUrl url, AutoNavigationReason reason)
    {
        if (reason == AutoNavigationReason.Skip)
            throw new ArgumentOutOfRangeException(nameof(reason));

        if (WhenAutoNavigated?.IsCompleted != true) {
            DebugLog?.LogDebug("NavigateTo({Url}, {Reason}): enqueuing initial navigation target", url, reason);
            lock (_lock) {
                if (!_autoNavigateStarted)
                    _initialNavigationTargets.Add((url, reason));
                else
                    // TODO(DF): To think how better gracefully handle this case.
                    Log.LogWarning("NavigateTo({Url}, {Reason}): enqueuing initial navigation target after auto navigation started", url, reason);
            }
            return;
        }

        if (reason == AutoNavigationReason.Initial) {
            DebugLog?.LogDebug("NavigateTo({Url}, {Reason}): skipped (initial navigation is already completed)", url, reason);
            return;
        }

        DebugLog?.LogDebug("NavigateTo({Url}, {Reason}): processing", url, reason);
        HandleNavigateTo(url, reason);
    }

    // Protected methods

    protected abstract Task HandleAutoNavigate(LocalUrl url, AutoNavigationReason reason, CancellationToken cancellationToken);
    protected abstract Task HandleNavigateTo(LocalUrl url, AutoNavigationReason reason);
}
