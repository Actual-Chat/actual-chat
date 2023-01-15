using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class NavbarUI
{
    private BrowserInfo BrowserInfo { get; }
    private ChatUI ChatUI { get; }
    private HistoryUI HistoryUI { get; }
    private NavigationManager Nav { get; }

    public bool IsVisible { get; private set; }
    public string ActiveGroupId { get; private set; } = "chats";
    public string ActiveGroupTitle { get; private set; } = "Chats";
    public Dictionary<string, Action> AddButtonAction { get; } = new (StringComparer.Ordinal);
    public event EventHandler? ActiveGroupChanged;
    public event EventHandler? VisibilityChanged;

    public NavbarUI(IServiceProvider services)
    {
        BrowserInfo = services.GetRequiredService<BrowserInfo>();
        ChatUI = services.GetRequiredService<ChatUI>();
        HistoryUI = services.GetRequiredService<HistoryUI>();
        Nav = services.GetRequiredService<NavigationManager>();

        HistoryUI.AfterLocationChangedHandled += OnAfterLocationChangedHandled;
        if (BrowserInfo.ScreenSize.Value.IsNarrow())
            IsVisible = ShouldShowNavbar();
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
            ChangeVisibilityInternal(visible);
            return;
        }

        if (visible)
            _ = HistoryUI.GoBack();
        else {
            var selectedChatId = ChatUI.SelectedChatId.Value;
            if (!selectedChatId.IsNone)
                Nav.NavigateTo(Links.Chat(selectedChatId));
        }
    }

    private void OnAfterLocationChangedHandled(object? sender, EventArgs e)
    {
        if (!BrowserInfo.ScreenSize.Value.IsNarrow())
            return;
        ChangeVisibilityInternal(ShouldShowNavbar());
    }

    private bool ShouldShowNavbar()
    {
        var navUrl = Nav.GetLocalUrl();
        return navUrl == Links.Chat(default);
    }

    private void ChangeVisibilityInternal(bool visible)
    {
        IsVisible = visible;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
