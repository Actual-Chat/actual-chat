using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Manages navigation bar group selection, title display, and navbar settings.
/// </summary>
public class NavbarUI : UIServiceBase<UIHub>
{
    // Dispatcher-only, like every mutation here. The selection is published for reads from any thread:
    // compute methods route on it (NavbarExt) while a worker may be changing it through SelectGroup
    private readonly List<Group> _groups = new ();
    private string _selectedGroupId = "";
    private string _selectedGroupTitle = "";

    public SyncedState<UserNavbarSettings> Settings { get; }
    public string SelectedGroupId => Volatile.Read(ref _selectedGroupId);
    public string SelectedGroupTitle => Volatile.Read(ref _selectedGroupTitle);
    public event EventHandler<NavbarGroupChangedEventArgs>? SelectedGroupChanged;
    public event EventHandler? SelectedGroupTitleUpdated;
    public Task WhenReady => Settings.WhenFirstTimeRead;

    public NavbarUI(UIHub hub) : base(hub)
    {
        Settings = StateFactory.NewUserSettingsSynced(
            UserSettingsUI,
            UserNavbarSettings.KvasKey,
            new UserNavbarSettings(),
            updateDelayer: FixedDelayer.NextTick,
            category: StateCategories.Get(GetType(), nameof(Settings)));
        Hub.RegisterDisposable(Settings);
    }

    // NOTE(AY): Reads are safe from any thread; every mutation runs on the Blazor Dispatcher's thread.
    // SelectGroup marshals itself there (ChatUI calls it from a worker), the rest expect to be on it already.

    public void InitSelectedGroup(string id)
    {
        Log.LogDebug("Init selected group id '{Id}', prev selected group id is '{PrevId}'", id, SelectedGroupId);
        if (!SelectedGroupId.IsNullOrEmpty())
            return;

        Volatile.Write(ref _selectedGroupId, id);
    }

    public void SelectGroup(string id, bool isUserAction)
    {
        if (!Dispatcher.CheckAccess()) {
            _ = Dispatcher.InvokeSafeAsync(() => SelectGroup(id, isUserAction), Log);
            return;
        }

        var group = _groups.FirstOrDefault(c => c.Id == id);
        Log.LogDebug("Group changed (Id='{Id}', Title='{Title}')", id, group?.Title ?? "(unknown)");
        Volatile.Write(ref _selectedGroupId, id);
        Volatile.Write(ref _selectedGroupTitle, group?.Title ?? string.Empty);
        SelectedGroupChanged?.Invoke(this, new NavbarGroupChangedEventArgs(id, isUserAction));
    }

    public void RegisterGroup(string id, string title)
    {
        var group = _groups.FirstOrDefault(c => c.Id == id);
        if (group == null) {
            group = new Group(id);
            _groups.Add(group);
        }
        group.Title = title;
        UpdateTitle(group.Id, group.Title);
    }

    public void UnregisterGroup(string id)
        => _groups.RemoveAll(c => c.Id == id);

    public void SetNavbarPinState(ChatId chatId, bool mustPin)
    {
        var pinnedChats = Settings.Value.PinnedChats;
        var isPinned = pinnedChats.Contains(chatId);
        if (isPinned == mustPin)
            return;

        var newPinnedChats = mustPin
            ? pinnedChats.With(chatId, true)
            : pinnedChats.Without(chatId);
        SetNavbarPinnedChats(newPinnedChats);
    }

    public void SetNavbarPinnedChats(IReadOnlyCollection<ChatId> pinnedChats)
        => Settings.Set(x => x.Value with { PinnedChats = pinnedChats.ToArray() });

    public void SetNavbarPlacesOrder(IReadOnlyCollection<PlaceId> places)
        => Settings.Set(x => x.Value with { PlacesOrder = places.ToArray() });

    private void UpdateTitle(string id, string title)
    {
        if (id != SelectedGroupId)
            return;

        if (title == SelectedGroupTitle)
            return;

        Log.LogDebug("Group title changed (Id='{Id}', Title='{Title}')", id, title);
        Volatile.Write(ref _selectedGroupTitle, title);
        SelectedGroupTitleUpdated?.Invoke(this, EventArgs.Empty);
    }

    private class Group(string id)
    {
        public string Id { get; } = id;
        public string Title { get; set; } = "";
    }
}

/// <summary>
/// Event arguments for navbar group selection changes.
/// </summary>
public class NavbarGroupChangedEventArgs(string id, bool isUserAction) : EventArgs
{
    public string Id { get; } = id;
    public bool IsUserAction { get; } = isUserAction;
}
