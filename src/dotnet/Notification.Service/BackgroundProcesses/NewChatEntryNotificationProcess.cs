using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Notification.Backend;

namespace ActualChat.Notification.BackgroundProcesses;

public class NewChatEntryNotificationProcess: AsyncProcessBase
{
    private readonly IChatEventStreamer<NewChatEntryEvent> _chatEventStreamer;
    private readonly IChatEventHandler<NewChatEntryEvent> _chatEventHandler;
    private readonly INotificationsBackend _notificationsBackend;

    public NewChatEntryNotificationProcess(
        IChatEventStreamer<NewChatEntryEvent> chatEventStreamer,
        IChatEventHandler<NewChatEntryEvent> chatEventHandler,
        INotificationsBackend notificationsBackend)
    {
        _chatEventStreamer = chatEventStreamer;
        _chatEventHandler = chatEventHandler;
        _notificationsBackend = notificationsBackend;
    }

    protected override Task RunInternal(CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
