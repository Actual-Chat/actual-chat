using ActualLab.Fusion.Operations.Internal;

namespace ActualChat.Queues.Internal;

public class EnqueueOnCompletionProcessor(IQueues queues, AgentInfo agentInfo)
    : IOperationCompletionListener
{
    private IQueues Queues { get; } = queues;
    private AgentInfo AgentInfo { get; } = agentInfo;

    public bool IsReady()
        => true;

    public Task OnOperationCompleted(IOperation operation, CommandContext? commandContext)
    {
        if (operation.AgentId != AgentInfo.Id)
            return Task.CompletedTask;

        var items = CollectEnqueueOnCompletionEntries(operation.Items);
        if (items == null || items.Count == 0)
            return Task.CompletedTask;

        // "switch" here just speeds up a few common cases
        switch (items.Count) {
        case 1:
            var command0 = items[0];
            return Queues.Enqueue(command0);
        case 2:
            command0 = items[0];
            var command1 = items[1];
            var task0 = Queues.Enqueue(command0);
            var task1 = Queues.Enqueue(command1);
            return Task.WhenAll(task0, task1);
        case 3:
            command0 = items[0];
            command1 = items[1];
            var command2 = items[1];
            task0 = Queues.Enqueue(command0);
            task1 = Queues.Enqueue(command1);
            var task2 = Queues.Enqueue(command2);
            return Task.WhenAll(task0, task1, task2);
        default:
            var tasks = items.Select(command => Queues.Enqueue(command));
            return Task.WhenAll(tasks);
        }
    }

    private static List<QueuedCommand>? CollectEnqueueOnCompletionEntries(
        OptionSet operationItems,
        List<QueuedCommand>? result = null)
    {
        if (operationItems.Items.Count == 0)
            return result;

        result ??= new();
        var entries = operationItems.Get<ImmutableList<QueuedCommand>>();
        if (entries != null && entries.Count != 0)
            result.AddRange(entries);

        var nestedCommandEntries = operationItems.Get<ImmutableList<NestedCommandEntry>>();
        if (nestedCommandEntries == null || nestedCommandEntries.Count == 0)
            return result;

        foreach (var nestedCommandEntry in nestedCommandEntries)
            CollectEnqueueOnCompletionEntries(nestedCommandEntry.Items, result);

        return result;
    }
}
