using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class NavbarUI : ScopedServiceBase<UIHub>
{
    private readonly IStoredState<Symbol> _selectedNavbarGroupId;

    public NavbarUI(UIHub hub) : base(hub)
        => _selectedNavbarGroupId = StateFactory.NewKvasStored<Symbol>(
            new (LocalSettings, nameof(SelectedNavbarGroupId)) {
                InitialValue = "chats",
            });

    public IState<Symbol> SelectedNavbarGroupId => _selectedNavbarGroupId;

    public void SelectGroup(string id, bool isUserAction)
    {
        if (OrdinalEquals(id, _selectedNavbarGroupId.Value))
            return;

        _selectedNavbarGroupId.Value = id;

        if (!isUserAction)
            return;

        if (Hub.PanelsUI.Left.IsSearchMode)
            Hub.PanelsUI.Left.SearchToggle();
        // if (Hub.NavbarUI.IsPinnedChatSelected(out _))
        //     Hub.PanelsUI.HidePanels();
    }
}
