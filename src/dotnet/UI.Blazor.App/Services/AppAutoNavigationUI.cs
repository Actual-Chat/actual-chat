using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class AppAutoNavigationUI : AutoNavigationUI
{
    public AppAutoNavigationUI(IServiceProvider services) : base(services) { }

    protected override async ValueTask<LocalUrl> GetDefaultAutoNavigationUrl()
    {
        var currentUrl = History.LocalUrl;
        if (!currentUrl.IsHome() && !currentUrl.IsChatRoot())
            return currentUrl;

        // You're at "/" or "/chat" URL
        try {
            var accountUI = Services.GetRequiredService<AccountUI>();
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
