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
    private History? _history;
    private AppBlazorCircuitContext? _blazorCircuitContext;

    protected readonly List<(LocalUrl Url, AutoNavigationReason Reason)> InitialNavigationTargets = new();
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
        DebugLog ??= Log.IsEnabled(LogLevel.Debug) ? Log : NullLogger.Instance;
    }

    public Task AutoNavigate(CancellationToken cancellationToken)
        => WhenAutoNavigated ??= HandleAutoNavigate(cancellationToken);

    public void DispatchNavigateTo(LocalUrl url, AutoNavigationReason reason)
    {
        if (Dispatcher.CheckAccess())
            NavigateTo(url, reason);
        else
            Dispatcher.InvokeAsync(() => NavigateTo(url, reason));
    }

    public void NavigateTo(LocalUrl url, AutoNavigationReason reason)
    {
        if (reason == AutoNavigationReason.Skip)
            throw new ArgumentOutOfRangeException(nameof(reason));

        if (WhenAutoNavigated?.IsCompleted != true) {
            DebugLog?.LogDebug("NavigateTo({Url}, {Reason}): enqueuing initial navigation target", url, reason);
            InitialNavigationTargets.Add((url, reason));
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

    protected abstract Task HandleAutoNavigate(CancellationToken cancellationToken);
    protected abstract Task HandleNavigateTo(LocalUrl url, AutoNavigationReason reason);
}
