using Stl.Fusion.Operations.Internal;

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
            var (command0, queueRef0) = items[0];
            return CommandQueues.Get(queueRef0).Enqueue(command0);
        case 2:
            (command0, queueRef0) = items[0];
            var (command1, queueRef1) = items[1];
            var task1 = CommandQueues.Get(queueRef0).Enqueue(command0);
            var task2 = CommandQueues.Get(queueRef1).Enqueue(command1);
            return Task.WhenAll(task1, task2);
        default:
            var tasks = items.Select(x => CommandQueues.Get(x.QueueRef).Enqueue(x.Command));
            return Task.WhenAll(tasks);
        }
    }

    private static List<EnqueuedCommandEntry>? CollectEnqueueOnCompletionEntries(
        OptionSet operationItems,
        List<EnqueuedCommandEntry>? result = null)
    {
        if (operationItems.Items.Count == 0)
            return result;

        result ??= new();
        var entries = operationItems.Get<ImmutableList<EnqueuedCommandEntry>>();
        if (entries != null && entries.Count != 0)
            result.AddRange(entries);

        var nestedCommandEntries = operationItems.Get<ImmutableList<NestedCommandEntry>>();
        if (nestedCommandEntries != null && nestedCommandEntries.Count != 0)
            foreach (var nestedCommandEntry in nestedCommandEntries)
                CollectEnqueueOnCompletionEntries(nestedCommandEntry.Items, result);

        return result;
    }
}
