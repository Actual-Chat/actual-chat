using ActualChat.Flows;
using ActualChat.Queues;

namespace ActualChat.Notifications.Flows;

[Flow(DelayQuanta = 30)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class NotificationFlow : Flow<Unit>
{
    private IChatPositionsBackend ChatPositionsBackend => field ??= Services.GetRequiredService<IChatPositionsBackend>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IQueues Queues => field ??= Services.Queues();
    private IUserPresences UserPresences => field ??= Services.GetRequiredService<IUserPresences>();
    private UrlMapper UrlMapper => field ??= Services.UrlMapper();
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory
        => field ??= Services.KeyedFactory<IBackendChatMarkupHub, ChatId>();

    public static string GetArguments(UserId userId, ChatId chatId)
        => FlowId.CombineArguments(userId.Value, chatId.Value);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var args = Id.SplitArguments();
        var userId = UserId.Parse(args[0]);
        var chatId = ChatId.Parse(args[1]);

        // Check if the user has unread entries
        var readPosition = await ChatPositionsBackend
            .Get(userId, chatId, ChatPositionKind.Read, cancellationToken)
            .ConfigureAwait(false);
        var newEntries = await ChatsBackend
            .ListNewEntries(chatId, readPosition.EntryLid, 20, cancellationToken)
            .ConfigureAwait(false);
        var entry = newEntries.FirstOrDefault(e => !e.IsSystemEntry);
        if (entry is null) {
            SetResult(default);
            return;
        }

        // Skip notification if the unread message is very fresh and the user is still online
        var entryAge = Hub.Clocks.SystemClock.Now - entry.BeginsAt;
        if (entryAge < Constants.Notification.ThrottleIntervals.Message) {
            var presence = await UserPresences.Get(userId, cancellationToken).ConfigureAwait(false);
            if (presence is Presence.Online or Presence.Recording) {
                SetResult(default);
                return;
            }
        }

        // User has an unread message — reconstruct and send the notification
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null || chat.HasSingleAuthor) {
            SetResult(default);
            return;
        }

        var author = await AuthorsBackend
            .Get(chatId, entry.AuthorId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (author is null) {
            SetResult(default);
            return;
        }

        var (content, _) = await NotificationHelper.GetText(entry, MarkupConsumer.Notification, ChatMarkupHubFactory, cancellationToken)
            .ConfigureAwait(false);

        var title = NotificationHelper.GetTitle(chat, author);
        var iconUrl = NotificationHelper.GetIconUrl(chat, author, UrlMapper);
        var now = Hub.Clocks.CoarseSystemClock.Now;
        var similarityKey = chatId.Value;

        var notificationId = NotificationId.New(userId, NotificationKind.Message, similarityKey);
        var notification = NotificationItem.New(notificationId, chatId, entry.LocalId, entry.AuthorId) with {
            Title = title,
            Text = content,
            IconUrl = iconUrl,
            SentAt = now,
        };

        await Queues.Enqueue(new NotificationsBackend_Notify(notification), cancellationToken).ConfigureAwait(false);
        SetResult(default);
    }

}
