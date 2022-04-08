using ActualChat.Chat.Events;
using ActualChat.Events;
using ActualChat.Notification.Backend;

namespace ActualChat.Chat.EventHandlers;

public class NewChatEntryEventHandler: IEventHandler<NewChatEntryEvent>
{
    private readonly IChatsBackend _chatsBackend;
    private readonly IChatAuthorsBackend _chatAuthorsBackend;

    public NewChatEntryEventHandler(IChatsBackend chatsBackend, IChatAuthorsBackend chatAuthorsBackend)
    {
        _chatsBackend = chatsBackend;
        _chatAuthorsBackend = chatAuthorsBackend;
    }

    public async Task Handle(NewChatEntryEvent @event, ICommander commander, CancellationToken cancellationToken)
    {
        var chatAuthor = await _chatAuthorsBackend.Get(@event.ChatId, @event.AuthorId, false, cancellationToken).ConfigureAwait(false);
        var userId = chatAuthor?.UserId;
        if (userId == null)
            return;

        var title = await GetTitle(@event.ChatId, cancellationToken).ConfigureAwait(false);
        var content = GetContent(@event.Content);
        var command = new INotificationsBackend.NotifySubscribersCommand(
            @event.ChatId,
            @event.Id,
            userId,
            title,
            content);
        await commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetTitle(string chatId, CancellationToken cancellationToken)
    {
        // TODO(AK): Internationalization
        var chat = await _chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        return chat?.Title ?? "New message";
    }

    private string GetContent(string chatEventContent)
    {
        if (chatEventContent.Length <= 1024)
            return chatEventContent;

        var lastSpaceIndex = chatEventContent.IndexOf(' ', 1000);
        return chatEventContent.Substring(0, lastSpaceIndex < 1024 ? lastSpaceIndex : 1000);
    }
}
