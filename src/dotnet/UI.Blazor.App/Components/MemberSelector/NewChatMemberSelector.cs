using ActualChat.UI.Blazor.App.Services;
using ActualChat.Contacts;

namespace ActualChat.UI.Blazor.App.Components;

internal sealed class NewChatMemberSelector(AppUIHub hub, ChatId chatId)
     : UIServiceBase<AppUIHub>(hub), IMemberSelector
{
    private IAuthors Authors => Hub.Authors;
    private IContacts Contacts => Hub.Contacts;
    private IPlaces Places => Hub.Places;

    public CandidateListKind CandidateListKind
        => chatId is PlaceChatId ? CandidateListKind.PlaceMembers : CandidateListKind.Contacts;

    public async Task<UserId[]> ListCandidateUserIds(CancellationToken cancellationToken)
    {
        if (chatId is PlaceChatId placeChatId) {
            var userIds = await Places.ListUserIds(Session, placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
            return userIds;
        }

        var contacts = await Contacts.ListUserContacts(Session, cancellationToken).ConfigureAwait(false);
        return contacts.Select(c => c.Account!.Id).ToArray();
    }

    public Task<UserId[]> ListMemberUserIds(CancellationToken cancellationToken)
        => Authors.ListUserIds(Session, chatId, cancellationToken);

    public async Task<Exception?> Invite(UserId[] userIds, CancellationToken cancellationToken) {
        var command = new Authors_Invite { Session = Session, ChatId = chatId, UserIds = userIds };
        var (_, error) = await UICommander.Run(command, cancellationToken).ConfigureAwait(false);
        return error;
    }
}
