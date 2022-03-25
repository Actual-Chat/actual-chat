using ActualChat.Chat;
using ActualChat.Chat.Events;

namespace ActualChat.Notification;

public class NewChatEntryNotificationGenerator: IChatEventNotificationGenerator<NewChatEntryEvent>
{
    private readonly IChatAuthorsBackend _chatAuthorsBackend;

    public NewChatEntryNotificationGenerator(IChatAuthorsBackend chatAuthorsBackend)
        => _chatAuthorsBackend = chatAuthorsBackend;

    // TODO(AK): support mentions
    // TODO(AK): throttle notifications
    public async IAsyncEnumerable<NotificationEntry> GenerateNotifications(
        NewChatEntryEvent chatEvent,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // TODO(AK): filter out authors with 'disable notifications' setting
        var authorIds = await _chatAuthorsBackend.GetAuthorIds(chatEvent.ChatId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var authorId in authorIds) {
            yield return new NotificationEntry();
        }

        throw new NotImplementedException();
    }
}
