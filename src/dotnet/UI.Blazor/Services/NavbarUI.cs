namespace ActualChat.UI.Blazor.Services;

public class NavbarUI(IServiceProvider services)
{
    private readonly List<Group> _groups = new ();
    private ILogger? _log;

    private IServiceProvider Services { get; } = services;
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public string SelectedGroupId { get; private set; } = "chats";
    public string SelectedGroupTitle { get; private set; } = "";
    public event EventHandler<NavbarGroupChangedEventArgs>? SelectedGroupChanged;
    public event EventHandler? SelectedGroupTitleUpdated;

    // NOTE(AY): Any public member of this type can be used only from Blazor Dispatcher's thread

    public void SelectGroup(string id, bool isUserAction)
    {
        if (OrdinalEquals(id, SelectedGroupId))
            return;

        var group = _groups.FirstOrDefault(c => OrdinalEquals(c.Id, id));
        Log.LogDebug("Group changed (Id='{Id}', Title='{Title}')", id, group?.Title ?? "(unknown)");
        SelectedGroupId = id;
        SelectedGroupTitle = group?.Title ?? string.Empty;
        SelectedGroupChanged?.Invoke(this, new NavbarGroupChangedEventArgs(id, isUserAction));
    }

    public void RegisterGroup(string id, string title)
    {
        var group = _groups.FirstOrDefault(c => OrdinalEquals(c.Id, id));
        if (group == null) {
            group = new Group(id);
            _groups.Add(group);
        }
        group.Title = title;
        UpdateTitle(group.Id, group.Title);
    }

    public void UnregisterGroup(string id)
        => _groups.RemoveAll(c => OrdinalEquals(c.Id, id));

    private void UpdateTitle(string id, string title)
    {
        if (!OrdinalEquals(id, SelectedGroupId))
            return;

        if (OrdinalEquals(title, SelectedGroupTitle))
            return;

        Log.LogDebug("Group title changed (Id='{Id}', Title='{Title}')", id, title);
        SelectedGroupTitle = title;
        SelectedGroupTitleUpdated?.Invoke(this, EventArgs.Empty);
    }

    private class Group(string id)
    {
        public string Id { get; } = id;
        public string Title { get; set; } = "";
    }
}

public class NavbarGroupChangedEventArgs(string id, bool isUserAction) : EventArgs
{
    public string Id { get; } = id;
    public bool IsUserAction { get; } = isUserAction;
}
