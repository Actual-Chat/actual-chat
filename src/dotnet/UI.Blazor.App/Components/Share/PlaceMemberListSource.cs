using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

internal class PlaceMemberListSource(AppUIHub hub, PlaceId placeId, UserId[] excludeUserIds)
    : UIServiceBase<AppUIHub>(hub), IMemberListSource
{
    private IPlaces Places => Hub.Places;

    public CandidateListKind CandidateListKind => CandidateListKind.PlaceMembers;

    public async Task<UserId[]> ListCandidateUserIds(CancellationToken cancellationToken)
    {
        var userIds = await Places.ListUserIds(Session, placeId, cancellationToken).ConfigureAwait(false);
        if (excludeUserIds.Length > 0)
            userIds = userIds.Except(excludeUserIds).ToArray();
        return userIds;
    }

    public Task<UserId[]> ListMemberUserIds(CancellationToken cancellationToken)
        => Task.FromResult(Array.Empty<UserId>());
}
