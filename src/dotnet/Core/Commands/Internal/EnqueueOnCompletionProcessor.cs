using ActualLab.Fusion.Operations.Internal;

namespace ActualChat.Commands.Internal;

public class EnqueueOnCompletionProcessor : IOperationCompletionListener
{
    private ICommandQueues CommandQueues { get; }
    private AgentInfo AgentInfo { get; }

    public EnqueueOnCompletionProcessor(ICommandQueues commandQueues, AgentInfo agentInfo)
    {
        CommandQueues = commandQueues;
        AgentInfo = agentInfo;
    }

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
            return CommandQueues[command0.QueueId].Enqueue(command0);
        case 2:
            command0 = items[0];
            var command1 = items[1];
            var task1 = CommandQueues[command0.QueueId].Enqueue(command0);
            var task2 = CommandQueues[command1.QueueId].Enqueue(command1);
            return Task.WhenAll(task1, task2);
        default:
            var tasks = items.Select(command => CommandQueues[command.QueueId].Enqueue(command));
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
