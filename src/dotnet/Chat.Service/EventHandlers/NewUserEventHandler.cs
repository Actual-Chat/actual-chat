using ActualChat.Events;
using ActualChat.Users.Events;

namespace ActualChat.Chat.EventHandlers;

public class NewUserEventHandler : IEventHandler<NewUserEvent>
{
    private readonly IChatsBackend _chatsBackend;
    private readonly IChatAuthorsBackend _chatAuthorsBackend;

    public NewUserEventHandler(IChatsBackend chatsBackend, IChatAuthorsBackend chatAuthorsBackend)
    {
        _chatsBackend = chatsBackend;
        _chatAuthorsBackend = chatAuthorsBackend;
    }

    public async Task Handle(NewUserEvent @event, ICommander commander, CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.AnnouncementsChatId;
        _ = await _chatAuthorsBackend.GetOrCreate(chatId, @event.UserId, false, cancellationToken).ConfigureAwait(false);
    }
}
