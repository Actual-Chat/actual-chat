using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.Chat;

public sealed class PlacesSettings (IServiceProvider services, AccountSettings accountSettings)
{
    private IServiceProvider Services { get; } = services;
    private AccountSettings AccountSettings { get; } = accountSettings;

    public async Task<PlaceId[]> GetPlacesOrder(Session session, PlaceId[] places, CancellationToken cancellationToken)
    {
        var placesSettings = await places
            .Select(id => AccountSettings.GetUserPlaceSettings(id, cancellationToken).AsTask())
            .Collect()
            .ConfigureAwait(false);
        return placesSettings
            .Select(e => e.OrderingHint)
            .Zip(places)
            .OrderBy(e=> e.First)
            .Select(e=> e.Second)
            .ToArray();
    }

    public async Task SetPlacesOrder(Session session, PlaceId[] placesOrder, CancellationToken cancellationToken = default)
    {
        var placesSettings = await placesOrder
            .Select(id => AccountSettings.GetUserPlaceSettings(id, cancellationToken).AsTask())
            .Collect()
            .ConfigureAwait(false);
        var orderingHint = 0;
        foreach (var e in placesSettings.Zip(placesOrder)) {
            orderingHint++;
            var userPlaceSettings = e.First;
            var placeId = e.Second;
            userPlaceSettings = userPlaceSettings with { OrderingHint = orderingHint };
            await AccountSettings.SetUserPlaceSettings(placeId, userPlaceSettings, cancellationToken).ConfigureAwait(false);
        }
    }
}
