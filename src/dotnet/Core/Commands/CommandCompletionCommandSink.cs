using Stl.Fusion.Operations.Internal;

namespace ActualChat.Commands;

public class CommandCompletionCommandSink : IOperationCompletionListener
{
    private ICommandQueueProvider CommandQueueProvider { get; }
    private AgentInfo AgentInfo { get; }

    public CommandCompletionCommandSink(ICommandQueueProvider commandQueueProvider, AgentInfo agentInfo)
    {
        CommandQueueProvider = commandQueueProvider;
        AgentInfo = agentInfo;
    }

    public bool IsReady()
        => true;

    public async Task OnOperationCompleted(IOperation operation, CommandContext? commandContext)
    {
        if (operation.AgentId != AgentInfo.Id)
            return;

        if (operation.Items.Items.Count == 0)
            return;

        var queuedCommands = operation.Items.Items.Values
            .OfType<QueuedCommand>()
            .Concat(operation.Items.Items.Values.OfType<ImmutableList<NestedCommandEntry>>()
                .SelectMany(l => l)
                .SelectMany(nce => nce.Items.Items.Values
                    .OfType<QueuedCommand>()));

        // TODO(AK): it's suspicious the we don't have CancellationToken there
        var cancellationToken = CancellationToken.None;
        var enqueueTasks = queuedCommands
            .SelectMany(qc => qc.QueueRefs, (c, r) => (c.Command, Queue: CommandQueueProvider.Get(r)))
            .Select(p => p.Queue.Enqueue(p.Command, cancellationToken))
            .ToList();
        switch (enqueueTasks.Count)
        {
        case 0:
            return;
        case 1:
            await enqueueTasks[0].ConfigureAwait(false);
            break;
        case 2:
            await TaskExt.WhenAll(enqueueTasks[0], enqueueTasks[1]).ConfigureAwait(false);
            break;
        default:
            await TaskExt.WhenAll(enqueueTasks).ConfigureAwait(false);
            break;
        }
    }
}
