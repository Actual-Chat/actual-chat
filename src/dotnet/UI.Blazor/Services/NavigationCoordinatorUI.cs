namespace ActualChat.UI.Blazor.Services;

public class NavigationCoordinatorUI
{
    private readonly TaskCompletionSource<Unit> _whenReadySource = TaskCompletionSourceExt.New<Unit>();
    private int _outstandingRequestsNumber;

    private Task WhenReady => _whenReadySource.Task;
    private IServiceProvider Services { get; }
    private History History { get; }
    private ILogger Log { get; }

    public NavigationCoordinatorUI(IServiceProvider services)
    {
        Services = services;
        History = Services.GetRequiredService<History>();
        Log = Services.GetRequiredService<ILogger<NavigationCoordinatorUI>>();
        var loadingUI =  Services.GetRequiredService<LoadingUI>();
        loadingUI.WhenLoaded.ContinueWith(
            _ => MarkAsReady(),
            TaskScheduler.Default);
    }

    public void MarkAsReady()
        => _whenReadySource.TrySetResult(default);

    public async Task HandleNavigationRequest(string relativeUrl)
    {
        var localUrl = new LocalUrl(relativeUrl);
        Log.LogInformation("HandleNavigationRequest. LocalUrl: '{LocalUrl}'", localUrl);
        _outstandingRequestsNumber++;

        // Navigation is allowed only after initial loading:
        // including account is loaded and initial redirect to '/chat/'
        await WhenReady.ConfigureAwait(true);
        await History.NavigateTo(localUrl).ConfigureAwait(true);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await History
            .When(_ => History.LocalUrl.Value.OrdinalStartsWith(localUrl), cts.Token)
            .SuppressCancellation().ConfigureAwait(true);
        await History.WhenNavigationCompleted.ConfigureAwait(true);
        _outstandingRequestsNumber--;

        if (!localUrl.IsChat())
            return;
        // If screen is narrow, ensure middle panel is visible after navigation to chat page
        var panelsUI = Services.GetRequiredService<PanelsUI>();
        panelsUI.Middle.EnsureVisible();
    }

    public bool HasOutstandingRequests()
        => _outstandingRequestsNumber > 0;
}
