namespace ActualChat.Commands;

public interface IEventHandler<in TEvent> : ICommandHandler<TEvent>
    where TEvent : class, IEvent
{
    Task OnEvent(TEvent @event, CommandContext context, CancellationToken cancellationToken);

    Task ICommandHandler<TEvent>.OnCommand(TEvent command, CommandContext context, CancellationToken cancellationToken)
        => OnEvent(command, context, cancellationToken);
}
