using ActualChat.Kvas;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatListView
{
    private readonly IStoredState<ChatListSettings> _settingsState;

    public PlaceId PlaceId { get; }

    public ChatListView(PlaceId placeId, IStoredState<ChatListSettings> settingsState)
    {
        PlaceId = placeId;
        _settingsState = settingsState;
    }

    public async Task<ChatListSettings> GetSettings(CancellationToken cancellationToken = default)
    {
        await _settingsState.WhenRead.ConfigureAwait(false);
        return await _settingsState.Use(cancellationToken).ConfigureAwait(false);
    }

    public void SetFilter(string filterId)
        => _settingsState.Set(filterId, static (filterId1, r) => {
            var settings = r.Value;
            if (settings.FilterId != filterId1)
                settings = settings with { FilterId = filterId1 };
            return settings;
        });

    public void SetOrder(ChatListOrder order)
        => _settingsState.Set(order, static (order1, r) => {
            var settings = r.Value;
            if (settings.Order != order1)
                settings = settings with { Order = order1 };
            return settings;
        });
}
