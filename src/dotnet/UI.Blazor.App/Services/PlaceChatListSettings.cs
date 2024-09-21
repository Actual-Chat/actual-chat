using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class PlaceChatListSettings
{
    private readonly IStoredState<ChatListSettings> _state;

    public PlaceId PlaceId { get; }
    public Task WhenReady => _state.WhenRead;

    public PlaceChatListSettings(PlaceId placeId, UIHub hub)
    {
        PlaceId = placeId;
        _state = hub.StateFactory().NewKvasStored<ChatListSettings>(
            new (hub.LocalSettings(), ChatListSettings.GetKvasKey(placeId)) {
                InitialValue = new(),
                Category = StateCategories.Get(GetType(), nameof(_state)),
            });
    }

    public async ValueTask<ChatListSettings> Get(CancellationToken cancellationToken = default)
    {
        if (!WhenReady.IsCompleted)
            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await _state.Use(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetFilter(string filterId, CancellationToken cancellationToken = default)
    {
        if (!WhenReady.IsCompleted)
            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        _state.Set(filterId,
            static (filterId1, r) => {
                var settings = r.Value;
                if (settings.FilterId != filterId1)
                    settings = settings with { FilterId = filterId1 };
                return settings;
            });
    }

    public async Task  SetOrder(ChatListOrder order, CancellationToken cancellationToken = default)
    {
        if (!WhenReady.IsCompleted)
            await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        _state.Set(order,
            static (order1, r) => {
                var settings = r.Value;
                if (settings.Order != order1)
                    settings = settings with { Order = order1 };
                return settings;
            });
    }
}
