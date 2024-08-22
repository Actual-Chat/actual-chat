using ActualChat.UI.Blazor.App.Services;
using ActualChat.Contacts;

namespace ActualChat.UI.Blazor.App.Components;

internal class NewChatMemberSelector(ChatUIHub hub, ChatId chatId) : IMemberSelector
{
    private Session Session { get; } = hub.Session();

    public CandidateListKind CandidateListKind
        => chatId.IsPlaceChat ? CandidateListKind.PlaceMembers : CandidateListKind.Contacts;

    public async Task<ApiArray<UserId>> ListCandidateUserIds(CancellationToken cancellationToken)
    {
        if (chatId.IsPlaceChat) {
            var userIds = await hub.Places.ListUserIds(Session, chatId.PlaceChatId.PlaceId, cancellationToken).ConfigureAwait(false);
            return userIds;
        }

        var contacts = await hub.Contacts.ListUserContacts(Session, cancellationToken).ConfigureAwait(false);
        return contacts.ToApiArray(c => c.Account!.Id);
    }

    public Task<ApiArray<UserId>> ListMemberUserIds(CancellationToken cancellationToken)
        => hub.Authors.ListUserIds(Session, chatId, cancellationToken);

    public async Task<Exception?> Invite(UserId[] userIds, CancellationToken cancellationToken) {
        var command = new Authors_Invite(Session, chatId, userIds);
        var (_, error) = await hub.UICommander().Run(command, cancellationToken).ConfigureAwait(false);
        return error;
    }
}
