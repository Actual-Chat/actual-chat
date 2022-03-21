namespace ActualChat.Commands;

public interface IQueueCommand
{
    CancellationToken CancellationToken { get; }
}

public sealed record CommandExecution
{
    public readonly IQueueCommand Command;
    internal readonly TaskCompletionSource _whenCommandProcessed;

    public CommandExecution(IQueueCommand command)
    {
        Command = command;
        /// TODO: use <see cref="TaskSource{T}"/> (?)
        _whenCommandProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Represents a task which will be completed when enqueued command is completed. <br/>
    /// </summary>
    public Task WhenCommandProcessed => _whenCommandProcessed.Task;

    public void Deconstruct(out IQueueCommand command, out Task whenCommandProcessed)
    {
        command = Command;
        whenCommandProcessed = WhenCommandProcessed;
    }
}
