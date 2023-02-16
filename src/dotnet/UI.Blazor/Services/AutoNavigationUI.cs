namespace ActualChat.UI.Blazor.Services;

public sealed class AutoNavigationUI
{
    private HistoryUI HistoryUI { get; }

    public bool MustNavigateToChatsOnSignIn { get; set; } = true;

    public AutoNavigationUI(IServiceProvider services)
        => HistoryUI = services.GetRequiredService<HistoryUI>();

    public bool TryNavigateToChatsOnSignIn()
    {
        if (!MustNavigateToChatsOnSignIn)
            return false;

        MustNavigateToChatsOnSignIn = false;
        HistoryUI.NavigateTo(Links.Chats);
        return true;
    }
}
