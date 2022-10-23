using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class NavbarUI
{
    protected BrowserInfo BrowserInfo { get; }
    protected ChatUI ChatUI { get; }
    protected HistoryUI HistoryUI { get; }
    protected NavigationManager Nav { get; }
    public bool IsVisible { get; private set; }
    public string ActiveGroupId { get; private set; } = "chats";
    public string ActiveGroupTitle { get; private set; } = "Chats";
    public Dictionary<string, Action> AddButtonAction { get; } = new (StringComparer.Ordinal);
    public event EventHandler? ActiveGroupChanged;
    public event EventHandler? VisibilityChanged;

    public NavbarUI(BrowserInfo browserInfo, HistoryUI historyUI, NavigationManager nav, ChatUI chatUI)
    {
        BrowserInfo = browserInfo;
        ChatUI = chatUI;
        HistoryUI = historyUI;
        Nav = nav;
        historyUI.AfterLocationChangedHandled += OnAfterLocationChangedHandled;
        if (BrowserInfo.ScreenSize.Value.IsNarrow())
            IsVisible = ShouldShowNavbar();
    }

    private void OnAfterLocationChangedHandled(object? sender, AfterLocationChangedHandledEventsArgs e)
    {
        if (!BrowserInfo.ScreenSize.Value.IsNarrow())
            return;
        InnerChangeVisibility(ShouldShowNavbar());
    }

    public void ActivateGroup(string id, string title)
    {
        if (OrdinalEquals(id, ActiveGroupId))
            return;

        ActiveGroupId = id;
        ActiveGroupTitle = title;
        ActiveGroupChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ChangeVisibility(bool visible)
    {
        if (IsVisible == visible)
            return;

        var screenSize = BrowserInfo.ScreenSize.Value;
        if (!screenSize.IsNarrow()) {
            InnerChangeVisibility(visible);
            return;
        }

        if (visible)
            _ = HistoryUI.GoBack();
        else {
            var selectedChatId = ChatUI.SelectedChatId.Value;
            if (!selectedChatId.IsEmpty)
                Nav.NavigateTo(Links.ChatPage(selectedChatId));
        }
    }

    private bool ShouldShowNavbar()
    {
        var relativeUrl = Nav.ToBaseRelativePath(Nav.Uri);
        var showNavbar = Links.Equals(relativeUrl, Links.ChatPage(""));
        return showNavbar;
    }

    private void InnerChangeVisibility(bool visible)
    {
        IsVisible = visible;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
