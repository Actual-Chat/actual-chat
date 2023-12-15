namespace ActualChat.UI.Blazor.Services;

public class NavbarUI(IServiceProvider services)
{
    private ILogger? _log;

    private IServiceProvider Services { get; } = services;
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public string SelectedGroupId { get; private set; } = "chats";
    public string SelectedGroupTitle { get; private set; } = "Chats";
    public event EventHandler? SelectedGroupChanged;
    public event EventHandler? SelectedGroupTitleUpdated;

    // NOTE(AY): Any public member of this type can be used only from Blazor Dispatcher's thread

    public void SelectGroup(string id, string title)
    {
        if (OrdinalEquals(id, SelectedGroupId))
            return;

        Log.LogDebug("Group changed (Id='{Id}', Title='{Title}')", id, title);
        SelectedGroupId = id;
        SelectedGroupTitle = title;
        SelectedGroupChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateTitle(string id, string title)
    {
        if (!OrdinalEquals(id, SelectedGroupId))
            return;

        if (OrdinalEquals(title, SelectedGroupTitle))
            return;

        Log.LogDebug("Group title changed (Id='{Id}', Title='{Title}')", id, title);
        SelectedGroupTitle = title;
        SelectedGroupTitleUpdated?.Invoke(this, EventArgs.Empty);
    }
}
