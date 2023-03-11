namespace ActualChat.UI.Blazor.Services;

public class NavbarUI
{
    public string SelectedGroupId { get; private set; } = "chats";
    public string SelectedGroupTitle { get; private set; } = "Chats";
    public event EventHandler? SelectedGroupChanged;

    // NOTE(AY): Any public member of this type can be used only from Blazor Dispatcher's thread

    public void SelectGroup(string id, string title)
    {
        if (OrdinalEquals(id, SelectedGroupId))
            return;

        SelectedGroupId = id;
        SelectedGroupTitle = title;
        SelectedGroupChanged?.Invoke(this, EventArgs.Empty);
    }
}
