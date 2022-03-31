namespace ActualChat.UI.Blazor.Services;

public class NavbarUI
{
    public string ActiveGroupId { get; private set; } = "chats";
    public string ActiveGroupTitle { get; private set; } = "Chats";
    public bool IsVisible { get; set; }
    public event EventHandler? ActiveGroupChanged;

    public void ActivateGroup(string id, string title)
    {
        if (string.Equals(id, ActiveGroupId, StringComparison.Ordinal)) return;
        ActiveGroupId = id;
        ActiveGroupTitle = title;
        ActiveGroupChanged?.Invoke(this, EventArgs.Empty);
    }
}
