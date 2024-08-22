using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

internal class PlaceMemberListSource(ChatUIHub hub, PlaceId placeId, UserId[] excludeUserIds) : IMemberListSource
{
    private Session Session { get; } = hub.Session();

    public CandidateListKind CandidateListKind => CandidateListKind.PlaceMembers;

    public async Task<ApiArray<UserId>> ListCandidateUserIds(CancellationToken cancellationToken)
    {
        var userIds = await hub.Places.ListUserIds(Session, placeId, cancellationToken).ConfigureAwait(false);
        if (excludeUserIds.Length > 0)
            userIds = userIds.Except(excludeUserIds).ToApiArray();
        return userIds;
    }

    public Task<ApiArray<UserId>> ListMemberUserIds(CancellationToken cancellationToken)
        => Task.FromResult(ApiArray<UserId>.Empty);
}
