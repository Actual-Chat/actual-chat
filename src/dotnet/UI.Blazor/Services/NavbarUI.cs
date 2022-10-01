namespace ActualChat.UI.Blazor.Services;

public class NavbarUI
{
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

        IsVisible = visible;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
