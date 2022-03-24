using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Notification.Backend;

namespace ActualChat.Notification.BackgroundProcesses;

public class InviteToChatNotificationProcess : AsyncProcessBase
{
    private readonly IChatEventStreamer<InviteToChatEvent> _chatEventStreamer;
    private readonly IChatEventHandler<InviteToChatEvent> _chatEventHandler;
    private readonly INotificationsBackend _notificationsBackend;

    public InviteToChatNotificationProcess(
        IChatEventStreamer<InviteToChatEvent> chatEventStreamer,
        IChatEventHandler<InviteToChatEvent> chatEventHandler,
        INotificationsBackend notificationsBackend)
    {
        _chatEventStreamer = chatEventStreamer;
        _chatEventHandler = chatEventHandler;
        _notificationsBackend = notificationsBackend;
    }

    protected override Task RunInternal(CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
