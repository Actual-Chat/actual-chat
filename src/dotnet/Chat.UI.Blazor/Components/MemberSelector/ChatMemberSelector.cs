using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Contacts;

namespace ActualChat.Chat.UI.Blazor.Components;

internal class ChatMemberSelector(ChatUIHub hub, ChatId chatId) : IMemberSelector
{
    private Session Session { get; } = hub.Session();

    public Task<ApiArray<UserId>> ListPreSelectedUserIds(CancellationToken cancellationToken)
        => hub.Authors.ListUserIds(Session, chatId, cancellationToken);

    public async Task<ApiArray<UserId>> ListUserIds(CancellationToken cancellationToken)
    {
        if (chatId.IsPlaceChat) {
            var userIds = await hub.Places.ListUserIds(Session, chatId.PlaceChatId.PlaceId, cancellationToken).ConfigureAwait(false);
            return userIds;
        }

        var contacts = await hub.Contacts.ListUserContacts(Session, cancellationToken).ConfigureAwait(false);
        return contacts.ToApiArray(c => c.Account!.Id);
    }

    public async Task<Exception?> Invite(UserId[] userIds, CancellationToken cancellationToken) {
        var command = new Authors_Invite(Session, chatId, userIds);
        var (_, error) = await hub.UICommander().Run(command, cancellationToken).ConfigureAwait(false);
        return error;
    }
}
