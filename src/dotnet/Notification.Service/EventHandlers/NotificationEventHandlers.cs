using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Notification.Backend;

namespace ActualChat.Notification.EventHandlers;

public class NotificationEventHandlers
{
    private INotificationsBackend NotificationsBackend { get; }
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private IChatsBackend ChatsBackend { get; }
    private ICommander Commander { get; }
    private ContentUrlMapper ContentUrlMapper { get; }

    public NotificationEventHandlers(
        INotificationsBackend notificationsBackend,
        IChatAuthorsBackend chatAuthorsBackend,
        IChatsBackend chatsBackend,
        ICommander commander,
        ContentUrlMapper contentUrlMapper)
    {
        NotificationsBackend = notificationsBackend;
        ChatAuthorsBackend = chatAuthorsBackend;
        ChatsBackend = chatsBackend;
        Commander = commander;
        ContentUrlMapper = contentUrlMapper;
    }

    [CommandHandler]
    public virtual async Task OnNewTextEntryEvent(
        NewTextEntryEvent @event,
        CancellationToken cancellationToken)
    {
        var (chatId, entryId, authorId, content) = @event;
        if (Computed.IsInvalidating())
            return;

        var chatAuthor = await ChatAuthorsBackend.Get(chatId, authorId, true, cancellationToken).ConfigureAwait(false);
        var userId = chatAuthor!.UserId;
        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        var title = GetTitle(chat, chatAuthor);
        var iconUrl = GetIconUrl(chat, chatAuthor);
        var textContent = GetContent(content);
        var notificationTime = DateTime.UtcNow;
        var userIds = await NotificationsBackend.ListSubscriberIds(chatId, cancellationToken).ConfigureAwait(false);
        foreach (var userIdGroup in userIds.Where(uid => !OrdinalEquals(uid, userId)).Chunk(200))
            await Task.WhenAll(userIdGroup.Select(uid
                    => Commander.Call(
                        new INotificationsBackend.NotifyUserCommand(
                            uid,
                            new NotificationEntry(
                                Ulid.NewUlid().ToString(),
                                NotificationType.Message,
                                title,
                                textContent,
                                iconUrl,
                                notificationTime) {
                                Message = new MessageNotificationEntry(chatId, entryId, authorId),
                            }),
                        cancellationToken)))
                .ConfigureAwait(false);
    }

    private string GetIconUrl(Chat.Chat chat, ChatAuthor chatAuthor)
         => chat.ChatType switch {
             ChatType.Group => !chat.Picture.IsNullOrEmpty() ? ContentUrlMapper.ContentUrl(chat.Picture) : "/favicon.ico",
             ChatType.Peer => !chatAuthor.Picture.IsNullOrEmpty() ? ContentUrlMapper.ContentUrl(chatAuthor.Picture) : "/favicon.ico",
             _ => throw new ArgumentOutOfRangeException(nameof(chat.ChatType), chat.ChatType, null),
         };

     private static string GetTitle(Chat.Chat chat, ChatAuthor chatAuthor)
         => chat.ChatType switch {
             ChatType.Group => $"{chatAuthor.Name} @ {chat.Title}",
             ChatType.Peer => $"{chatAuthor.Name}",
             _ => throw new ArgumentOutOfRangeException(nameof(chat.ChatType), chat.ChatType, null)
         };

     private static string GetContent(string chatEventContent)
     {
         var markup = MarkupParser.ParseRaw(chatEventContent);
         markup = new MarkupTrimmer(100).Rewrite(markup);
         return MarkupFormatter.ReadableUnstyled.Format(markup);
     }
}
