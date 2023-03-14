namespace ActualChat.UI.Blazor.Services;

public sealed class AutoNavigationUI
{
    private History History { get; }

    public bool MustNavigateToChatsOnSignIn { get; set; } = true;

    public AutoNavigationUI(IServiceProvider services)
        => History = services.GetRequiredService<History>();

    public bool TryNavigateToChatsOnSignIn()
    {
        if (!MustNavigateToChatsOnSignIn)
            return false;

        MustNavigateToChatsOnSignIn = false;
        History.NavigateTo(Links.Chats, true);
        return true;
    }
}
