using ActualChat.Chat.Events;
using ActualChat.Events;
using ActualChat.Notification.Backend;

namespace ActualChat.Chat.EventHandlers;

public class NewChatEntryEventHandler: IEventHandler<NewChatEntryEvent>
{
    private IChatsBackend ChatsBackend { get; }
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private ContentUrlMapper ContentUrlMapper { get; }
    private ILogger<NewChatEntryEventHandler> Log { get; }

    public NewChatEntryEventHandler(IChatsBackend chatsBackend, IChatAuthorsBackend chatAuthorsBackend, ContentUrlMapper contentUrlMapper, ILogger<NewChatEntryEventHandler> log)
    {
        ChatsBackend = chatsBackend;
        ChatAuthorsBackend = chatAuthorsBackend;
        ContentUrlMapper = contentUrlMapper;
        Log = log;
    }

    public async Task Handle(NewChatEntryEvent @event, ICommander commander, CancellationToken cancellationToken)
    {
        Log.LogInformation("Preparing notification about entryId='{ChatEntryId} in chat id='{ChatId}'", @event.Id, chat.Id);

        var chatAuthor = await ChatAuthorsBackend.Get(@event.ChatId, @event.AuthorId, true, cancellationToken)
            .Require()
            .ConfigureAwait(false);
        var chat = await ChatsBackend.Get(@event.ChatId, cancellationToken).Require().ConfigureAwait(false);

        var title = GetTitle(chat, chatAuthor);
        var iconUrl = GetIconUrl(chat, chatAuthor);
        var content = GetContent(@event.Content);
        var command = new INotificationsBackend.NotifySubscribersCommand(
            @event.ChatId,
            @event.Id,
            chatAuthor.UserId,
            title,
            iconUrl,
            content);
        await commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    private string GetIconUrl(Chat chat, ChatAuthor chatAuthor)
        => chat.ChatType switch {
            ChatType.Group => !chat.Picture.IsNullOrEmpty() ? ContentUrlMapper.ContentUrl(chat.Picture) : "/favicon.ico",
            ChatType.Peer => !chatAuthor.Picture.IsNullOrEmpty() ? ContentUrlMapper.ContentUrl(chatAuthor.Picture) : "/favicon.ico",
            _ => throw new ArgumentOutOfRangeException(nameof(chat.ChatType), chat.ChatType, null),
        };

    private string GetTitle(Chat chat, ChatAuthor chatAuthor)
        => chat.ChatType switch {
            ChatType.Group => $"{chatAuthor.Name} @ {chat.Title}",
            ChatType.Peer => $"{chatAuthor.Name}",
            _ => throw new ArgumentOutOfRangeException(nameof(chat.ChatType), chat.ChatType, null)
        };

    private string GetContent(string chatEventContent)
    {
        if (chatEventContent.Length <= 1024)
            return chatEventContent;

        var lastSpaceIndex = chatEventContent.IndexOf(' ', 1000);
        return chatEventContent.Substring(0, lastSpaceIndex < 1024 ? lastSpaceIndex : 1000);
    }
}
