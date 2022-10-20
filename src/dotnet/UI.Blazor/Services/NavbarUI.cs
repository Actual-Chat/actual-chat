namespace ActualChat.UI.Blazor.Services;

public class NavbarUI
{
    public NavbarUI(BrowserInfo browserInfo, HistoryUI historyUI)
    {
        BrowserInfo = browserInfo;
        HistoryUI = historyUI;
        if (BrowserInfo.ScreenSize.Value.IsNarrow())
            IsVisible = true;
    }

    protected HistoryUI HistoryUI { get; }
    protected BrowserInfo BrowserInfo { get; }
    public bool IsVisible { get; private set; }
    public string ActiveGroupId { get; private set; } = "chats";
    public string ActiveGroupTitle { get; private set; } = "Chats";
    public Dictionary<string, Action> AddButtonAction { get; } = new (StringComparer.Ordinal);
    public event EventHandler? ActiveGroupChanged;
    public event EventHandler? VisibilityChanged;

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
                    () => InnerChangeVisibility(false),
                    () => InnerChangeVisibility(true));
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
