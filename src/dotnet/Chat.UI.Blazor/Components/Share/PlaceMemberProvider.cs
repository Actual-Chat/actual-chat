using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

internal class PlaceMemberProvider(ChatUIHub hub, PlaceId placeId, UserId[] excludeUserIds) : IMemberSelectorDataProvider
{
    private Session Session { get; } = hub.Session();

    public Task<ApiArray<UserId>> ListPreSelectedUserIds(CancellationToken cancellationToken)
        => Task.FromResult(ApiArray<UserId>.Empty);

    public async Task<ApiArray<UserId>> ListUserIds(CancellationToken cancellationToken)
    {
        var userIds = await hub.Places.ListUserIds(Session, placeId, cancellationToken).ConfigureAwait(false);
        if (excludeUserIds.Length > 0)
            userIds = userIds.Except(excludeUserIds).ToApiArray();
        return userIds;
    }
}
