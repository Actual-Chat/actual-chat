using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public sealed class AutoNavigationUI
{
    private Session Session { get; }
    private IAccounts Accounts { get; }
    private NavigationManager Nav { get; }

    public bool MustNavigateToChatsOnSignIn { get; set; } = true;

    public AutoNavigationUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        Nav = services.GetRequiredService<NavigationManager>();
    }

    public void TryNavigateToChatsOnSignIn()
    {
        if (!MustNavigateToChatsOnSignIn)
            return;

        MustNavigateToChatsOnSignIn = false;
        Nav.NavigateTo(Links.Chat(default));
    }
}
