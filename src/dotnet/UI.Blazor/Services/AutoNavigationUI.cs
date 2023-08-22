using Stl.Diagnostics;

namespace ActualChat.UI.Blazor.Services;

public enum AutoNavigationReason
{
    Unknown = 0,
    SecondAutoNavigation = 1,
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

    public Task<LocalUrl> GetAutoNavigationUrl(CancellationToken cancellationToken = default)
        => Dispatcher.InvokeAsync(async () => {
// #if IOS
//             retrun await GetDefaultAutoNavigationUrl();
// #else
            if (_autoNavigationCandidates == null)
                throw StandardError.Internal($"{nameof(GetAutoNavigationUrl)} is called twice.");

            var defaultUrl = await GetDefaultAutoNavigationUrl();
            Log.LogInformation($"{nameof(GetAutoNavigationUrl)}: {{DefaultUrl}}", defaultUrl);
            await Services.GetRequiredService<AutoNavigationTasks>().Complete().WaitAsync(cancellationToken);
            Log.LogInformation($"{nameof(GetAutoNavigationUrl)}: AutoNavigationTasks are completed");
            var candidates = Interlocked.Exchange(ref _autoNavigationCandidates, null);
            if (candidates == null)
                throw StandardError.Internal($"{nameof(GetAutoNavigationUrl)} is called twice.");
            Log.LogInformation($"{nameof(GetAutoNavigationUrl)}: navigation candidates are reset");

            var url = candidates.Count > 0
                ? candidates.MaxBy(t => (int)t.Reason).Url
                : defaultUrl;
            Log.LogInformation($"{nameof(GetAutoNavigationUrl)}: {{AutoNavigationUrl}}", url);
            return url;
// #endif

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
// #if IOS
//         Log.LogInformation("* NavigateTo({Url}, {Reason})", url, reason);
//         return History.NavigateTo(url);
// #else
        Dispatcher.AssertAccess();
        if (_autoNavigationCandidates == null) {
            // Initial navigation already happened
            Log.LogInformation("* NavigateTo({Url}, {Reason})", url, reason);
            return History.NavigateTo(url);
        }

        // Initial navigation haven't happened yet
        Log.LogInformation("+ NavigateTo({Url}, {Reason})", url, reason);
        _autoNavigationCandidates.Add((url, reason));
        return Task.CompletedTask;
// #endif
    }

    // Protected methods

    protected abstract ValueTask<LocalUrl> GetDefaultAutoNavigationUrl();
}
