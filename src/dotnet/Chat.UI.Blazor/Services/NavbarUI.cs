using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class NavbarUI
{
    private ChatUI ChatUI { get; }
    private HistoryUI HistoryUI { get; }
    private NavigationManager Nav { get; }
    private BrowserInfo BrowserInfo { get; }

    public bool IsVisible { get; private set; }
    public string SelectedGroupId { get; private set; } = "chats";
    public string SelectedGroupTitle { get; private set; } = "Chats";
    public event EventHandler? SelectedGroupChanged;
    public event EventHandler? VisibilityChanged;

    public NavbarUI(IServiceProvider services)
    {
        ChatUI = services.GetRequiredService<ChatUI>();
        HistoryUI = services.GetRequiredService<HistoryUI>();
        Nav = services.GetRequiredService<NavigationManager>();
        BrowserInfo = services.GetRequiredService<BrowserInfo>();

        HistoryUI.AfterLocationChangedHandled += OnAfterLocationChangedHandled;
        if (BrowserInfo.ScreenSize.Value.IsNarrow())
            IsVisible = ShouldBeVisible();
    }

    // NOTE(AY): Any public member of this type can be used only from Blazor Dispatcher's thread

    public void SelectGroup(string id, string title)
    {
        if (OrdinalEquals(id, SelectedGroupId))
            return;

        SelectedGroupId = id;
        SelectedGroupTitle = title;
        SelectedGroupChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ChangeVisibility(bool visible, bool disableHistory = false)
    {
        if (IsVisible == visible)
            return;

        disableHistory |= BrowserInfo.ScreenSize.Value.IsWide();
        if (disableHistory) {
            SetIsVisible(visible);
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

    // Private methods

    private bool ShouldBeVisible()
    {
        var navUrl = Nav.GetLocalUrl();
        return navUrl == Links.Chats;
    }

    private void SetIsVisible(bool visible)
    {
        IsVisible = visible;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    // Event handlers

    private void OnAfterLocationChangedHandled(object? sender, EventArgs e)
    {
        if (!BrowserInfo.ScreenSize.Value.IsNarrow())
            return;

        SetIsVisible(ShouldBeVisible());
    }
}
