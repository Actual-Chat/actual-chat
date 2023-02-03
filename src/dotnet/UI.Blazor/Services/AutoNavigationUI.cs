namespace ActualChat.UI.Blazor.Services;

public sealed class AutoNavigationUI
{
    private NavigationManager Nav { get; }

    public bool MustNavigateToChatsOnSignIn { get; set; } = true;

    public AutoNavigationUI(IServiceProvider services)
        => Nav = services.GetRequiredService<NavigationManager>();

    public bool TryNavigateToChatsOnSignIn()
    {
        if (!MustNavigateToChatsOnSignIn)
            return false;

        MustNavigateToChatsOnSignIn = false;
        Nav.NavigateTo(Links.Chat(default));
        return true;
    }
}
