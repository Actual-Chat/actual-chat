using ActualChat.UI.Blazor.App.Services;
using ActualChat.Contacts;

namespace ActualChat.UI.Blazor.App.Components;

internal class NewPlaceMemberSelector(ChatUIHub hub, PlaceId placeId) : IMemberSelector
{
    private Session Session { get; } = hub.Session();

    public CandidateListKind CandidateListKind => CandidateListKind.Contacts;

    public async Task<UserId[]> ListCandidateUserIds(CancellationToken cancellationToken)
    {
        var contacts = await hub.Contacts.ListUserContacts(Session, cancellationToken).ConfigureAwait(false);
        return contacts.Select(c => c.Account!.Id).ToArray();
    }

    public async Task<UserId[]> ListMemberUserIds(CancellationToken cancellationToken)
        => await hub.Places.ListUserIds(Session, placeId, cancellationToken);

    public async Task<Exception?> Invite(UserId[] userIds, CancellationToken cancellationToken) {
        var command = new Places_Invite(Session, placeId, userIds);
        var (_, error) = await hub.UICommander().Run(command, cancellationToken).ConfigureAwait(false);
        return error;
    }
}
