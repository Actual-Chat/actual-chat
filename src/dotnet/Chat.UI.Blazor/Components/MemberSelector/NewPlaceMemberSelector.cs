using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Contacts;

namespace ActualChat.Chat.UI.Blazor.Components;

internal class NewPlaceMemberSelector(ChatUIHub hub, PlaceId placeId) : IMemberSelector
{
    private Session Session { get; } = hub.Session();

    public async Task<ApiArray<UserId>> ListPreSelectedUserIds(CancellationToken cancellationToken)
        => await hub.Places.ListUserIds(Session, placeId, cancellationToken);

    public async Task<ApiArray<UserId>> ListUserIds(CancellationToken cancellationToken)
    {
        var contacts = await hub.Contacts.ListUserContacts(Session, cancellationToken).ConfigureAwait(false);
        return contacts.ToApiArray(c => c.Account!.Id);
    }

    public async Task<Exception?> Invite(UserId[] userIds, CancellationToken cancellationToken) {
        var command = new Places_Invite(Session, placeId, userIds);
        var (_, error) = await hub.UICommander().Run(command, cancellationToken).ConfigureAwait(false);
        return error;
    }
}
