using ActualChat.Hosting;

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

public class AutoNavigationUI(UIHub hub) : ScopedServiceBase<UIHub>(hub)
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
            Log.LogInformation($"{nameof(GetAutoNavigationUrl)}. Default url: {{DefaultUrl}}", defaultUrl);

            if (HostInfo.HostKind.IsApp()) {
                var appNavigationTasks = AppNavigationQueue.DequeueAll(Services);
                Log.LogInformation(
                    $"{nameof(GetAutoNavigationUrl)}: AppNavigationQueue has {{Count}} tasks",
                    appNavigationTasks.Count);
                await Task.WhenAll(appNavigationTasks).ConfigureAwait(false);
            }

            var candidates = Interlocked.Exchange(ref _autoNavigationCandidates, null);
            if (candidates == null)
                throw StandardError.Internal($"{nameof(GetAutoNavigationUrl)} is called twice.");
            Log.LogInformation($"{nameof(GetAutoNavigationUrl)}: navigation candidates are reset");

            var url = candidates.Count > 0
                ? candidates
                    .Select((t, i) => new { t.Reason, t.Url, Index = i })
                    .OrderByDescending(t => t.Reason)
                    .ThenByDescending(t => t.Index)
                    .First().Url
                : defaultUrl;
            Log.LogInformation($"{nameof(GetAutoNavigationUrl)}: {{AutoNavigationUrl}}", url);
            return url;
        });

    public Task DispatchNavigateTo(string url, AutoNavigationReason reason)
    {
        Log.LogInformation("DispatchNavigateTo, Url: '{Url}', Reason: '{Reason}'", url, reason);

        // This method can be invoked from any synchronization context
        Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri);
        if (uri == null)
            return Task.CompletedTask;

        LocalUrl localUrl;
        if (uri.IsAbsoluteUri) {
            var tempLocalUrl = LocalUrl.FromAbsolute(url, UrlMapper);
            if (tempLocalUrl is null)
                return Task.CompletedTask;

            localUrl = tempLocalUrl.Value;
        }
        else
            localUrl = new LocalUrl(url, ParseOrNone.Option);

        if (reason == AutoNavigationReason.Notification && !localUrl.IsChat()) {
            Log.LogWarning("NavigateTo LocalUrl: '{LocalUrl}' for notification reason is forbidden", localUrl);
            return Task.CompletedTask;
        }

        return DispatchNavigateTo(localUrl, reason);
    }

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

    protected virtual async ValueTask<LocalUrl> GetDefaultAutoNavigationUrl()
    {
        var currentUrl = History.LocalUrl;
        if (!currentUrl.IsHome() && !currentUrl.IsChatRoot())
            return currentUrl;

        // You're at "/" or "/chat" URL
        try {
            var accountUI = Hub.AccountUI;
            await accountUI.WhenLoaded.WaitAsync(TimeSpan.FromMilliseconds(2000)).ConfigureAwait(false);
            var ownAccount = accountUI.OwnAccount.Value;
            return ownAccount.IsGuestOrNone
                ? currentUrl
                : Links.Chats; // You're signed in - so we redirect you to /chats/
        }
        catch (TimeoutException) {
            return currentUrl;
        }
    }
}
