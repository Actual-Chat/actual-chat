using ActualChat.Chat.Events;

namespace ActualChat.Notification;

public interface IChatEventHandler<T> where T : IChatEvent
{
    [CommandHandler]
    public Task Notify(NotifyCommand command, CancellationToken cancellationToken);

    public record NotifyCommand(T Event) : ICommand<Unit>, IBackendCommand;
}
