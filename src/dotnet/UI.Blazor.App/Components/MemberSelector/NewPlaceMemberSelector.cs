using ActualChat.UI.Blazor.App.Services;
using ActualChat.Contacts;

namespace ActualChat.UI.Blazor.App.Components;

internal sealed class NewPlaceMemberSelector(AppUIHub hub, PlaceId placeId)
    : UIServiceBase<AppUIHub>(hub), IMemberSelector
{
    private IContacts Contacts => Hub.Contacts;
    private IPlaces Places => Hub.Places;

    public CandidateListKind CandidateListKind => CandidateListKind.Contacts;

    public async Task<UserId[]> ListCandidateUserIds(CancellationToken cancellationToken)
    {
        var contacts = await Contacts.ListUserContacts(Session, cancellationToken).ConfigureAwait(false);
        return contacts.Select(c => c.Account!.Id).ToArray();
    }

    public async Task<UserId[]> ListMemberUserIds(CancellationToken cancellationToken)
        => await Places.ListUserIds(Session, placeId, cancellationToken);

    public async Task<Exception?> Invite(UserId[] userIds, CancellationToken cancellationToken) {
        var command = new Places_Invite { Session = Session, PlaceId = placeId, UserIds = userIds };
        var (_, error) = await UICommander.Run(command, cancellationToken).ConfigureAwait(false);
        return error;
    }
}
