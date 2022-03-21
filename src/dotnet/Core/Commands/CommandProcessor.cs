namespace ActualChat.Commands;

public interface ICommandProcessor<TQueueCommand>
    where TQueueCommand : IQueueCommand
{
    Task OnCommandLoopStarted(CancellationToken cancellationToken);
    Task ProcessCommand(
        TQueueCommand command,
        CancellationToken cancellationToken);
    Task OnCommandLoopCompeted(CancellationToken cancellationToken);
}

public abstract class CommandProcessor<TQueueCommand> : ICommandProcessor<TQueueCommand>
    where TQueueCommand : IQueueCommand
{
    public virtual Task OnCommandLoopStarted(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public abstract Task ProcessCommand(
        TQueueCommand command,
        CancellationToken cancellationToken);

    public virtual Task OnCommandLoopCompeted(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
