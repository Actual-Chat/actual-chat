using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class NavbarUI : ScopedServiceBase<ChatUIHub>
{
    public const string PlacePrefix = "place-";
    public const string PinnedChatPrefix = "pinned-chat-";

    private readonly IStoredState<Symbol> _selectedNavbarGroupId;
    private readonly ISyncedState<UserNavbarSettings> _navbarSettings;

    public NavbarUI(ChatUIHub hub) : base(hub)
    {
        _selectedNavbarGroupId = StateFactory.NewKvasStored<Symbol>(
            new (LocalSettings, nameof(SelectedNavbarGroupId)) {
                InitialValue = "",
            });
        _navbarSettings = StateFactory.NewKvasSynced<UserNavbarSettings>(
            new (AccountSettings, UserNavbarSettings.KvasKey) {
                InitialValue = new UserNavbarSettings(),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(NavbarSettings)),
            });

        Hub.RegisterDisposable(_selectedNavbarGroupId);
        Hub.RegisterDisposable(_navbarSettings);
    }

    public Task WhenLoaded => _selectedNavbarGroupId.WhenRead;
    public IState<Symbol> SelectedNavbarGroupId => _selectedNavbarGroupId;
    public IState<UserNavbarSettings> NavbarSettings => _navbarSettings;

    public void SelectGroup(string id, bool isUserAction)
    {
        if (OrdinalEquals(id, _selectedNavbarGroupId.Value))
            return;

        _selectedNavbarGroupId.Value = id;

        if (!isUserAction)
            return;

        var placeId = PlaceId.None;
        var isChats = IsGroupSelected(NavbarGroupIds.Chats) || IsPlaceSelected(out placeId);
        if (IsPinnedChatSelected(out var pinnedChatId)) {
            isChats = true;
            placeId = pinnedChatId.PlaceId;
        }

        if (!isChats)
            return;

        Hub.ChatUI.SelectChat(pinnedChatId, placeId);
        //
        // if (Hub.PanelsUI.Left.IsSearchMode)
        //     Hub.PanelsUI.Left.SearchToggle();
        // if (IsPinnedChatSelected(out _))
        //     Hub.PanelsUI.HidePanels();
    }

    public void SetNavbarPinState(ChatId chatId, bool mustPin)
    {
        if (chatId.IsNone)
            return;

        var pinnedChats = NavbarSettings.Value.PinnedChats;
        var isPinned = pinnedChats.Contains(chatId);
        if (isPinned == mustPin)
            return;

        var newPinnedChats = mustPin
            ? pinnedChats.Add(chatId, true)
            : pinnedChats.RemoveAll(chatId);
        SetNavbarPinnedChats(newPinnedChats);
    }

    public void SetNavbarPinnedChats(IReadOnlyCollection<ChatId> pinnedChats)
        => _navbarSettings.Value = _navbarSettings.Value with { PinnedChats = pinnedChats.ToApiArray() };

    public void SetNavbarPlacesOrder(IReadOnlyCollection<PlaceId> places)
        => _navbarSettings.Value = _navbarSettings.Value with { PlacesOrder = places.ToApiArray() };

    public bool IsGroupSelected(string id)
        => OrdinalEquals(SelectedNavbarGroupId.Value, id);

    public bool IsPlaceSelected(out PlaceId placeId)
    {
        placeId = PlaceId.None;
        string groupId = SelectedNavbarGroupId.Value;
        if (!groupId.OrdinalStartsWith(PlacePrefix))
            return false;

        var sPlaceId = groupId.Substring(PlacePrefix.Length);
        placeId = new PlaceId(sPlaceId, AssumeValid.Option);
        return true;
    }

    public bool IsPinnedChatSelected(out ChatId chatId)
    {
        chatId = ChatId.None;
        string groupId = SelectedNavbarGroupId.Value;
        if (!groupId.OrdinalStartsWith(PinnedChatPrefix))
            return false;

        var sChatId = groupId.Substring(PinnedChatPrefix.Length);
        chatId = ChatId.ParseOrNone(sChatId);
        return !chatId.IsNone;
    }
}
