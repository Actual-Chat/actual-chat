namespace ActualChat.UI.Blazor.Services;

public class NavbarService
{
    public string ActiveGroupId { get; private set; } = "chats";
    public string ActiveGroupTitle { get; private set; } = "Chats";
    public event EventHandler ActiveGroupChanged = (s, e) => { };

    public void ActivateGroup(string id, string title)
    {
        if (string.Equals(id, ActiveGroupId, StringComparison.Ordinal)) return;
        ActiveGroupId = id;
        ActiveGroupTitle = title;
        ActiveGroupChanged.Invoke(this, EventArgs.Empty);
    }
}
