namespace ActualChat.UI.Blazor.Services;

public class NavbarUI
{
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public string SelectedGroupId { get; private set; } = "chats";
    public string SelectedGroupTitle { get; private set; } = "Chats";
    public event EventHandler? SelectedGroupChanged;

    public NavbarUI(IServiceProvider services)
        => Services = services;

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
}
