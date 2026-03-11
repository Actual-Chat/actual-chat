using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.Queues;
using ActualChat.Users;

namespace ActualChat.Notification.Flows;

[Flow(DelayQuanta = 10)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class NotificationFlow : Flow<Unit>
{
    private IChatPositionsBackend ChatPositionsBackend => field ??= Services.GetRequiredService<IChatPositionsBackend>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IQueues Queues => field ??= Services.Queues();
    private UrlMapper UrlMapper => field ??= Services.UrlMapper();
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory
        => field ??= Services.KeyedFactory<IBackendChatMarkupHub, ChatId>();

    public static string GetArguments(UserId userId, ChatId chatId, long entryLocalId, NotificationKind kind, AuthorId authorId)
        => FlowId.CombineArguments(userId.Value, chatId.Value, entryLocalId.ToString(CultureInfo.InvariantCulture), ((int)kind).ToString(CultureInfo.InvariantCulture), authorId.Value);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var args = Id.SplitArguments();
        var userId = UserId.Parse(args[0]);
        var chatId = ChatId.Parse(args[1]);
        var entryLocalId = long.Parse(args[2], CultureInfo.InvariantCulture);
        var kind = (NotificationKind)int.Parse(args[3], CultureInfo.InvariantCulture);
        var authorId = AuthorId.Parse(args[4]);

        // Check if the user has already read the message
        var readPosition = await ChatPositionsBackend
            .Get(userId, chatId, ChatPositionKind.Read, cancellationToken)
            .ConfigureAwait(false);
        if (readPosition.EntryLid >= entryLocalId) {
            SetResult(default);
            return;
        }

        // User hasn't read the message — reconstruct and send the notification
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null) {
            SetResult(default);
            return;
        }

        var author = await AuthorsBackend
            .Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (author is null) {
            SetResult(default);
            return;
        }

        var entryId = ChatEntryId.New(chatId, entryLocalId);
        var entry = await ChatsBackend.GetEntry(entryId, cancellationToken).ConfigureAwait(false);
        if (entry is null) {
            SetResult(default);
            return;
        }

        var (content, _) = await NotificationHelper.GetText(entry, MarkupConsumer.Notification, ChatMarkupHubFactory, cancellationToken)
            .ConfigureAwait(false);

        var title = NotificationHelper.GetTitle(chat, author);
        var iconUrl = NotificationHelper.GetIconUrl(chat, author, UrlMapper);
        var now = Hub.Clocks.CoarseSystemClock.Now;
        var similarityKey = chatId.Value;

        var notificationId = NotificationId.New(userId, kind, similarityKey);
        var notification = new Notification(notificationId) {
            Title = title,
            Content = content,
            IconUrl = iconUrl,
            SentAt = now,
            ChatEntryNotification = new ChatEntryNotificationOption(entryId, authorId),
        };

        await Queues.Enqueue(new NotificationsBackend_Notify(notification), cancellationToken).ConfigureAwait(false);
        SetResult(default);
    }

}
