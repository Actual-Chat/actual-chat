using ActualLab.CommandR.Operations;

namespace ActualChat.Queues.Internal;

public class EnqueueOnCompletionProcessor(IQueues queues, HostId hostId)
    : IOperationCompletionListener
{
    private IQueues Queues { get; } = queues;
    private HostId HostId { get; } = hostId;

    public Task OnOperationCompleted(Operation operation, CommandContext? commandContext)
    {
        if (operation.HostId != HostId.Id)
            return Task.CompletedTask;

        var commands = CollectQueuedCommands(operation);
        if (commands.Count == 0)
            return Task.CompletedTask;

        // "switch" here just speeds up a few common cases
        switch (commands.Count) {
        case 1:
            var command0 = commands[0];
            return Queues.Enqueue(command0);
        case 2:
            command0 = commands[0];
            var command1 = commands[1];
            var task0 = Queues.Enqueue(command0);
            var task1 = Queues.Enqueue(command1);
            return Task.WhenAll(task0, task1);
        case 3:
            command0 = commands[0];
            command1 = commands[1];
            var command2 = commands[1];
            task0 = Queues.Enqueue(command0);
            task1 = Queues.Enqueue(command1);
            var task2 = Queues.Enqueue(command2);
            return Task.WhenAll(task0, task1, task2);
        default:
            var tasks = commands.Select(command => Queues.Enqueue(command));
            return Task.WhenAll(tasks);
        }
    }

    private static List<QueuedCommand> CollectQueuedCommands(
        Operation operation,
        List<QueuedCommand>? result = null)
    {
        result ??= new();
        var entries = operation.Items.Get<ImmutableList<QueuedCommand>>();
        if (entries != null && entries.Count != 0)
            result.AddRange(entries);

        foreach (var (command, items) in operation.NestedOperations) {
            var nestedEntries = items.Get<ImmutableList<QueuedCommand>>();
            if (nestedEntries != null && nestedEntries.Count != 0)
                result.AddRange(nestedEntries);
        }
        return result;
    }
}
