using ActualLab.Diagnostics;

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

public abstract class AutoNavigationUI(UIHub hub) : ScopedServiceBase<UIHub>(hub)
{
    private volatile List<(LocalUrl Url, AutoNavigationReason Reason)>? _autoNavigationCandidates = new();

    protected History History => Hub.History;
    protected AppBlazorCircuitContext CircuitContext => Hub.CircuitContext;
    protected Dispatcher Dispatcher => Hub.Dispatcher;

    public Task<LocalUrl> GetAutoNavigationUrl(CancellationToken cancellationToken = default)
        => Dispatcher.InvokeAsync(async () => {
            if (_autoNavigationCandidates == null)
                throw StandardError.Internal($"{nameof(GetAutoNavigationUrl)} is called twice.");

            var defaultUrl = await GetDefaultAutoNavigationUrl().ConfigureAwait(false);
            Log.LogInformation($"{nameof(GetAutoNavigationUrl)}: {{DefaultUrl}}", defaultUrl);
            await Services.GetRequiredService<AutoNavigationTasks>().Complete().WaitAsync(cancellationToken).ConfigureAwait(false);
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
        });

    public Task DispatchNavigateTo(LocalUrl url, AutoNavigationReason reason)
    {
        if (CircuitContext.WhenReady.IsCompleted)
            return Dispatcher.CheckAccess()
                ? NavigateTo(url, reason)
                : Dispatcher.InvokeAsync(() => NavigateTo(url, reason));

        return Task.Run(async () => {
            await CircuitContext.WhenReady.ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => NavigateTo(url, reason)).ConfigureAwait(false);
        });
    }

    public Task NavigateTo(LocalUrl url, AutoNavigationReason reason)
    {
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
    }

    // Protected methods

    protected abstract ValueTask<LocalUrl> GetDefaultAutoNavigationUrl();
}
