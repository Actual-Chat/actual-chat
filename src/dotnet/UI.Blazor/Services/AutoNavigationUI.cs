
namespace ActualChat.UI.Blazor.Services;

public enum AutoNavigationReason
{
    Unknown = 0,
    SecondAutoNavigation = 1,
    SignIn = 10,
    FixedChatId = 20,
    Notification = 50,
    AppLink = 51,
    Share = 52,
    Invite = 53,
    SignOut = 100,
}

public sealed class AutoNavigationUI(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private volatile List<(LocalUrl Url, AutoNavigationReason Reason)>? _autoNavigationCandidates = new();
    private AttentionHold? _attentionHold;

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

            var (url, reason) = candidates.Count > 0
                ? candidates
                    .Select((t, i) => (t.Url, t.Reason, Index: i))
                    .OrderByDescending(t => t.Reason)
                    .ThenByDescending(t => t.Index)
                    .Select(t => (t.Url, t.Reason))
                    .First()
                : (defaultUrl, AutoNavigationReason.Unknown);
            Log.LogInformation($"{nameof(GetAutoNavigationUrl)}: {{AutoNavigationUrl}}", url);
            if (HoldsAttention(reason))
                HoldAttentionAt(url, reason);
            return url;
        });

    // Raised instead of a navigation when the URL an attention-holding tap leads to is already open
    public event Action<LocalUrl>? NavigatedToCurrentUrl;

    public Task DispatchNavigateTo(string url, AutoNavigationReason reason)
    {
        Log.LogInformation("DispatchNavigateTo, Url: '{Url}', Reason: '{Reason}'", url, reason);

        // This method can be invoked from any synchronization context
        if (!TryGetLocalUrl(url, out var localUrl)) {
            Log.LogError("Could not get LocalUrl from url: '{Url}'", url);
            return Task.CompletedTask;
        }

        if (reason == AutoNavigationReason.Notification && !localUrl.IsChat() && !localUrl.IsNotification()) {
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

    public Task NavigateTo(LocalUrl url, AutoNavigationReason reason, bool mustReplace = false)
    {
        Dispatcher.AssertAccess();
        if (_autoNavigationCandidates == null) {
            // Initial navigation already happened
            Log.LogInformation("* NavigateTo({Url}, {Reason})", url, reason);
            if (HoldsAttention(reason))
                HoldAttentionAt(url, reason);
            if (!HoldsAttention(reason) || History.LocalUrl != url)
                return History.NavigateTo(url, mustReplace);

            // The URL is already open, so there is no navigation to hide the panels or to carry a jump
            Hub.PanelsUI.HidePanels();
            NavigatedToCurrentUrl?.Invoke(url);
            return Task.CompletedTask;
        }

        // Initial navigation hasn't happened yet
        Log.LogInformation("+ NavigateTo({Url}, {Reason})", url, reason);
        _autoNavigationCandidates.Add((url, reason));
        return Task.CompletedTask;
    }

    // Holds unsolicited UI back while the user is at what they came for. A newer hold replaces
    // the older one; it is taken first, so nothing slips in between.
    public void HoldAttentionAt(LocalUrl url, AutoNavigationReason reason)
    {
        var hold = Hub.AttentionUI.Hold($"{reason}: {url}", leaveTarget: url);
        Interlocked.Exchange(ref _attentionHold, hold)?.Dispose();
    }

    // Private methods

    private static bool HoldsAttention(AutoNavigationReason reason)
        => reason is AutoNavigationReason.Notification
            or AutoNavigationReason.AppLink
            or AutoNavigationReason.Share
            or AutoNavigationReason.Invite;

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
