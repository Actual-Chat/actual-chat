namespace ActualChat.UI.Blazor.Services;

public class NavbarUI
{
    protected HistoryUI HistoryUI { get; }
    protected BrowserInfo BrowserInfo { get; }
    public bool IsVisible { get; private set; }
    public string ActiveGroupId { get; private set; } = "chats";
    public string ActiveGroupTitle { get; private set; } = "Chats";
    public Dictionary<string, Action> AddButtonAction { get; } = new (StringComparer.Ordinal);
    public event EventHandler? ActiveGroupChanged;
    public event EventHandler? VisibilityChanged;

    public bool PreventNavbarClose { get; set; }

    public NavbarUI(BrowserInfo browserInfo, HistoryUI historyUI, NavigationManager nav)
    {
        BrowserInfo = browserInfo;
        HistoryUI = historyUI;
        historyUI.AfterLocationChangedHandled += OnAfterLocationChangedHandled;
        if (BrowserInfo.ScreenSize.Value.IsNarrow())
            IsVisible = true;
    }

    private void OnAfterLocationChangedHandled(object? sender, AfterLocationChangedHandledEventsArgs e)
    {
        if (!BrowserInfo.ScreenSize.Value.IsNarrow())
            return;
        if (e.IsBackward)
            return;
        if (PreventNavbarClose)
            PreventNavbarClose = false;
        else
            ChangeVisibility(false);
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
        if (screenSize.IsNarrow()) {
            if (visible) {
                _ = HistoryUI.GoBack();
            } else {
                HistoryUI.NavigateTo(
                    () => {
                        InnerChangeVisibility(false);
                    },
                    () => {
                        PreventNavbarClose = true;
                        InnerChangeVisibility(true);
                    });
            }
        }
        else {
            InnerChangeVisibility(visible);
        }
    }

    private void InnerChangeVisibility(bool visible)
    {
        IsVisible = visible;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
