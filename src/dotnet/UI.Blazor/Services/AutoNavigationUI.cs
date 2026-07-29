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

public sealed class AutoNavigationUI(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private volatile List<(LocalUrl Url, AutoNavigationReason Reason)>? _autoNavigationCandidates = new();

    public Task<LocalUrl> GetAutoNavigationUrl()
        => Dispatcher.InvokeAsync(async () => {
            if (_autoNavigationCandidates == null)
                throw StandardError.Internal($"{nameof(GetAutoNavigationUrl)} is called twice.");

            var defaultUrl = GetDefaultAutoNavigationUrl();
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
        if (!TryGetLocalUrl(url, out var localUrl)) {
            Log.LogError("Could not get LocalUrl from url: '{Url}'", url);
            return Task.CompletedTask;
        }

        if (reason == AutoNavigationReason.Notification && !localUrl.IsChat()) {
            Log.LogWarning("NavigateTo LocalUrl: '{LocalUrl}' for notification reason is restricted", localUrl);
            return Task.CompletedTask;
        }

        return DispatchNavigateTo(localUrl, reason);
    }

    public Task DispatchNavigateTo(LocalUrl url, AutoNavigationReason reason)
    {
        if (Hub.WhenInitialized.IsCompleted)
            return Dispatcher.CheckAccess()
                ? NavigateTo(url, reason)
                : Dispatcher.InvokeAsync(() => NavigateTo(url, reason));

        return Task.Run(async () => {
            await Hub.WhenInitialized.ConfigureAwait(false);
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

        // Initial navigation hasn't happened yet
        Log.LogInformation("+ NavigateTo({Url}, {Reason})", url, reason);
        _autoNavigationCandidates.Add((url, reason));
        return Task.CompletedTask;
    }

    // Private methods

    private LocalUrl GetDefaultAutoNavigationUrl()
    {
        var currentUrl = History.LocalUrl;
        if (!currentUrl.IsHome() && !currentUrl.IsChatRoot())
            return currentUrl;

        // We're at "/" or "/chat" URL
        var accountUI = Hub.AccountUI;
        if (!accountUI.WhenReady.IsCompleted) {
            // Technically, it's impossible to land here: AppScopedServiceStarter.PrepareFirstRender
            // awaits for AccountUI to be ready before calling GetAutoNavigationUrl (and thus this method).
            return currentUrl;
        }

        var ownAccount = accountUI.OwnAccount.Value;
        return ownAccount.IsGuest
            ? currentUrl
            : Links.Chats; // We're signed in - so we redirect you to /chats/
    }

    private bool TryGetLocalUrl(string url, out LocalUrl localUrl)
    {
        Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri);
        if (uri == null) {
            localUrl = Links.Home;
            return false;
        }

        if (uri.IsAbsoluteUri) {
            var tempLocalUrl = LocalUrl.FromAbsolute(url, UrlMapper);
            if (tempLocalUrl is null) {
                localUrl = Links.Home;
                return false;
            }

            localUrl = tempLocalUrl.Value;
        }
        else
            localUrl = new LocalUrl(url);

        // The url comes from notifications and deep links, so it may carry anything
        if (localUrl.IsTrulyLocal())
            return true;

        localUrl = Links.Home;
        return false;
    }
}
