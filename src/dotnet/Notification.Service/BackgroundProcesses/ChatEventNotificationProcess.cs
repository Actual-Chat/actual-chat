using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Notification.Backend;

namespace ActualChat.Notification.BackgroundProcesses;

public class ChatEventNotificationProcess<T> : WorkerBase where T: IChatEvent
{
    private readonly IChatEventStreamer<T> _chatEventStreamer;
    private readonly IChatEventHandler<T> _chatEventHandler;

    public ChatEventNotificationProcess(
        IChatEventStreamer<T> chatEventStreamer,
        IChatEventHandler<T> chatEventHandler)
    {
        _chatEventStreamer = chatEventStreamer;
        _chatEventHandler = chatEventHandler;
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        // TODO(AK): persist/retrieve streamPosition
        var chatEvents = _chatEventStreamer.Read("0-0", cancellationToken);
        await foreach(var (chatEvent, _) in chatEvents.ConfigureAwait(false))
            await _chatEventHandler.Notify(new IChatEventHandler<T>.NotifyCommand(chatEvent), cancellationToken).ConfigureAwait(false);
    }
}
