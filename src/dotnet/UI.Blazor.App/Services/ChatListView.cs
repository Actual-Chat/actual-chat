using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

public class ChatListView(PlaceId placeId, IStoredState<ChatListSettings> settingsState)
{
    public PlaceId PlaceId { get; } = placeId;

    public Task WhenReady => settingsState.WhenRead;

    public async Task<ChatListSettings> GetSettings(CancellationToken cancellationToken = default)
    {
        await settingsState.WhenRead.ConfigureAwait(false);
        return await settingsState.Use(cancellationToken).ConfigureAwait(false);
    }

    public void SetFilter(string filterId)
        => settingsState.Set(filterId, static (filterId1, r) => {
            var settings = r.Value;
            if (settings.FilterId != filterId1)
                settings = settings with { FilterId = filterId1 };
            return settings;
        });

    public void SetOrder(ChatListOrder order)
        => settingsState.Set(order, static (order1, r) => {
            var settings = r.Value;
            if (settings.Order != order1)
                settings = settings with { Order = order1 };
            return settings;
        });
}
