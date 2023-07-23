using Stl.Diagnostics;

namespace ActualChat.UI.Blazor.Services;

public enum AutoNavigationReason
{
    Unknown = 0,
    SecondaryAutoNavigation = 1,
    SignIn = 10,
    FixedChatId = 20,
    Notification = 50,
    AppLink = 51,
    SignOut = 100,
}

public abstract class AutoNavigationUI : IHasServices
{
    private AppBlazorCircuitContext? _blazorCircuitContext;
    private volatile List<(LocalUrl Url, AutoNavigationReason Reason)>? _autoNavigationCandidates = new();

    protected ILogger Log { get; }
    protected ILogger? DebugLog { get; }

    public IServiceProvider Services { get; }
    public History History { get; }
    public AppBlazorCircuitContext BlazorCircuitContext => _blazorCircuitContext ??= Services.GetRequiredService<AppBlazorCircuitContext>();
    public Dispatcher Dispatcher => BlazorCircuitContext.Dispatcher;

    protected AutoNavigationUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        History = services.GetRequiredService<History>();
        DebugLog = Log.IfEnabled(LogLevel.Debug);
    }

    public Task<LocalUrl> GetAutoNavigationUrl(CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(async () => {
            if (_autoNavigationCandidates == null)
                throw StandardError.Internal($"{nameof(GetAutoNavigationUrl)} is called twice.");

            var defaultUrl = await GetDefaultAutoNavigationUrl();
            await Services.GetRequiredService<AutoNavigationTasks>().Complete().WaitAsync(cancellationToken);
            var candidates = Interlocked.Exchange(ref _autoNavigationCandidates, null);
            if (candidates == null)
                throw StandardError.Internal($"{nameof(GetAutoNavigationUrl)} is called twice.");

            var url = candidates.Count > 0
                ? candidates.MaxBy(t => (int) t.Reason).Url
                : defaultUrl;
            Log.LogInformation("Auto navigation URL: {AutoNavigationUrl}", url);
            return url;
        });

    public Task DispatchNavigateTo(LocalUrl url, AutoNavigationReason reason)
    {
        if (BlazorCircuitContext.WhenReady.IsCompleted)
            return Dispatcher.CheckAccess()
                ? NavigateTo(url, reason)
                : Dispatcher.InvokeAsync(() => NavigateTo(url, reason));

        return Task.Run(async () => {
            await BlazorCircuitContext.WhenReady.ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => NavigateTo(url, reason));
        });
    }

    public Task NavigateTo(LocalUrl url, AutoNavigationReason reason)
    {
        Dispatcher.AssertAccess();
        if (_autoNavigationCandidates == null) {
            // Initial navigation already happened
            Log.LogInformation("* NavigateTo({Url}, {Reason})", url, reason);
            return History.NavigateTo(url).SuppressExceptions();
        }

        // Initial navigation haven't happened yet
        Log.LogInformation("+ NavigateTo({Url}, {Reason})", url, reason);
        _autoNavigationCandidates.Add((url, reason));
        return History.WhenReady;
    }

    // Protected methods

    protected abstract ValueTask<LocalUrl> GetDefaultAutoNavigationUrl();
}
