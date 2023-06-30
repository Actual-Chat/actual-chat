using ActualChat.Chat.UI.Blazor.Services;
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
        var accountUI = Services.GetRequiredService<AccountUI>();
        await accountUI.WhenLoaded.ConfigureAwait(false);
        var ownAccount = accountUI.OwnAccount.Value;
        return ownAccount.IsGuestOrNone
            ? currentUrl
            : Links.Chats; // You're signed in - so we redirect you to /chats/
    }

    protected override async ValueTask<LocalUrl> FixUrl(LocalUrl url, CancellationToken cancellationToken)
    {
        if (!url.IsChatRoot())
            return url;

        var chatUI = Services.GetRequiredService<ChatUI>();
        await chatUI.WhenLoaded.ConfigureAwait(false);
        return Links.Chat(chatUI.SelectedChatId.Value);
    }
}
