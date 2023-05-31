using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class AppAutoNavigationUI : AutoNavigationUI
{
    public AppAutoNavigationUI(IServiceProvider services) : base(services) { }

    protected override async Task HandleAutoNavigate(LocalUrl url, AutoNavigationReason reason, CancellationToken cancellationToken)
    {
        if (reason == AutoNavigationReason.Skip) {
            // No default auto-navigation
            var currentUrl = History.LocalUrl;
            if (!currentUrl.IsHome() && !currentUrl.IsChatRoot())
                return;

            // You're at "/" or "/chat" URL
            var accountUI = Services.GetRequiredService<AccountUI>();
            var ownAccount = accountUI.OwnAccount.Value;
            if (ownAccount.IsGuestOrNone)
                return;

            // AND you're signed in - so we redirect you to /chats/
            url = Links.Chats;
        }

        var fixedUrl = await FixUrl(url, cancellationToken).ConfigureAwait(true);
        if (History.LocalUrl == fixedUrl) {
            Log.LogInformation("HandleAutoNavigate: no navigation needed");
            return;
        }

        // The part below is tricky: we want to wait for actual NavigationManager.Location change
        var nav = History.Nav;
        var whenNavigatedSource = TaskCompletionSourceExt.New<Unit>();
        var onLocationChanged = (EventHandler<LocationChangedEventArgs>)OnLocationChanged;
        nav.LocationChanged += onLocationChanged;
        try {
            Log.LogInformation("AutoNavigate to {Url}", url);
            await HandleNavigateTo(url, AutoNavigationReason.Initial).ConfigureAwait(false);
            await whenNavigatedSource.Task
                .WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(true);
            Log.LogInformation("AutoNavigate to {Url}: completed", url);
        }
        catch (TimeoutException) {
            Log.LogInformation("AutoNavigate to {Url}: timed out", url);
        }
        finally {
            nav.LocationChanged -= onLocationChanged;
        }

        void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            var navUrl = nav.GetLocalUrl();
            if (navUrl == fixedUrl)
                whenNavigatedSource.TrySetResult(default);
        }
    }

    protected override async Task HandleNavigateTo(LocalUrl url, AutoNavigationReason reason)
    {
        var cancellationToken = BlazorCircuitContext.StopToken;
        var mustReplace = reason == AutoNavigationReason.Initial;
        var originalUrl = url;
        url = await FixUrl(url, cancellationToken).ConfigureAwait(true);

        DebugLog?.LogDebug("HandleNavigateTo: {Url}, mustReplace = {MustReplace}", url, mustReplace);
        await History.NavigateTo(url, mustReplace).ConfigureAwait(true);

        if (url.IsChat()) {
            if (url != originalUrl) {
                // Original URL was either home or chat root page
                if (reason == AutoNavigationReason.Initial)
                    InitialLeftPanelIsVisible = true; // Ensures left panel is open when PanelsUI is loaded
            }
            else {
                // Ensure middle panel is visible after we land on /chat/<chatId> page
                var panelsUI = Services.GetRequiredService<PanelsUI>();
                panelsUI.HidePanels();
            }
        }
    }

    private async ValueTask<LocalUrl> FixUrl(LocalUrl url, CancellationToken cancellationToken)
    {
        if (!url.IsChatRoot())
            return url;

        var chatUI = Services.GetRequiredService<ChatUI>();
        var defaultChatId = await chatUI.GetDefaultChatId(cancellationToken).ConfigureAwait(true);
        return Links.Chat(defaultChatId);
    }
}
