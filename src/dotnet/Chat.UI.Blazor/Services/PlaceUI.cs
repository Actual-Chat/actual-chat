using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class PlaceUI
{
    private readonly IMutableState<PlaceId> _activePlaceId;

    private NavbarUI NavbarUI { get; }
    private ILogger Log { get; }

    public IServiceProvider Services { get; }
    public IState<PlaceId> ActivePlaceId => _activePlaceId;

    public PlaceUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        NavbarUI = services.GetRequiredService<NavbarUI>();

        _activePlaceId = Services.StateFactory().NewMutable(PlaceId.None);
        NavbarUI.SelectedGroupChanged += NavbarUIOnSelectedGroupChanged;
    }

    private void NavbarUIOnSelectedGroupChanged(object? sender, EventArgs e)
    {
        var placeId = PlaceId.None;
        if (NavbarUI.SelectedGroupId.OrdinalStartsWith(NavbarGroupIds.PlacePrefix)) {
            var sPlaceId = NavbarUI.SelectedGroupId.Substring(NavbarGroupIds.PlacePrefix.Length);
            placeId = new PlaceId(sPlaceId, AssumeValid.Option);
        }
        if (_activePlaceId.Value != placeId)
            _activePlaceId.Value = placeId;
    }
}
