using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Manages navigation bar group selection, title display, and navbar settings.
/// </summary>
public class NavbarUI : UIServiceBase<UIHub>
{
    private readonly List<Group> _groups = new ();

    public SyncedState<UserNavbarSettings> Settings { get; }
    public string SelectedGroupId { get; private set; } = "";
    public string SelectedGroupTitle { get; private set; } = "";
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

    // NOTE(AY): Any public member of this type can be used only from Blazor Dispatcher's thread

    public void InitSelectedGroup(string id)
    {
        Log.LogDebug("Init selected group id '{Id}', prev selected group id is '{PrevId}'", id, SelectedGroupId);
        if (!SelectedGroupId.IsNullOrEmpty())
            return;

        SelectedGroupId = id;
    }

    public void SelectGroup(string id, bool isUserAction)
    {
        var group = _groups.FirstOrDefault(c => c.Id == id);
        Log.LogDebug("Group changed (Id='{Id}', Title='{Title}')", id, group?.Title ?? "(unknown)");
        SelectedGroupId = id;
        SelectedGroupTitle = group?.Title ?? string.Empty;
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

    public void SetNavbarPlacement(bool isNarrow, NavbarPlacement placement)
        => Settings.Set(x => isNarrow
            ? x.Value with { NarrowPlacement = placement }
            : x.Value with { WidePlacement = placement });

    public void SetIsAccountButtonFirst(bool value)
        => Settings.Set(x => x.Value with { IsAccountButtonFirst = value });

    public void SetIsMultiRow(bool value)
        => Settings.Set(x => x.Value with { IsMultiRow = value });

    private void UpdateTitle(string id, string title)
    {
        if (id != SelectedGroupId)
            return;

        if (title == SelectedGroupTitle)
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

/// <summary>
/// Event arguments for navbar group selection changes.
/// </summary>
public class NavbarGroupChangedEventArgs(string id, bool isUserAction) : EventArgs
{
    public string Id { get; } = id;
    public bool IsUserAction { get; } = isUserAction;
}
