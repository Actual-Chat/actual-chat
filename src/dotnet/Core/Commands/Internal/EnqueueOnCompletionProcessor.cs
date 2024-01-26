using ActualLab.Fusion.Operations.Internal;

namespace ActualChat.Commands.Internal;

public class EnqueueOnCompletionProcessor(ICommandQueues commandQueues, AgentInfo agentInfo, ICommandQueueIdProvider queueIdProvider)
    : IOperationCompletionListener
{
    private ICommandQueues CommandQueues { get; } = commandQueues;
    private AgentInfo AgentInfo { get; } = agentInfo;
    private ICommandQueueIdProvider QueueIdProvider { get; } = queueIdProvider;

    public bool IsReady()
        => true;

    public Task OnOperationCompleted(IOperation operation, CommandContext? commandContext)
    {
        if (operation.AgentId != AgentInfo.Id)
            return Task.CompletedTask;

        var items = CollectEnqueueOnCompletionEntries(operation.Items);
        if (items == null || items.Count == 0)
            return Task.CompletedTask;

        switch (items.Count) {
        case 1:
            var command0 = items[0];
            var queueId0 = GetQueueId(command0);
            return CommandQueues[queueId0].Enqueue(command0);
        case 2:
            command0 = items[0];
            var command1 = items[1];
            var queueId1 = GetQueueId(command0);
            var queueId2 = GetQueueId(command1);
            var task1 = CommandQueues[queueId1].Enqueue(command0);
            var task2 = CommandQueues[queueId2].Enqueue(command1);
            return Task.WhenAll(task1, task2);
        default:
            var tasks = items.Select(command => CommandQueues[GetQueueId(command)].Enqueue(command));
            return Task.WhenAll(tasks);
        }
    }

    private QueueId GetQueueId(QueuedCommand queueIdProvider)
        => QueueIdProvider.Get(queueIdProvider);

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
