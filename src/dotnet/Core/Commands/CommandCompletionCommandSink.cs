namespace ActualChat.Commands;

public class CommandCompletionCommandSink : IOperationCompletionListener
{
    private LocalCommandQueue CommandQueue { get; }
    private AgentInfo AgentInfo { get; }

    public CommandCompletionCommandSink(LocalCommandQueue commandQueue, AgentInfo agentInfo)
    {
        CommandQueue = commandQueue;
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

        var eventConfigurations = operation.Items.Items.Values
            .OfType<ICommandConfiguration>();

        foreach (var eventConfiguration in eventConfigurations)
            // TODO(AK): it's suspicious the we don't have CancellationToken there
            await CommandQueue.Enqueue(eventConfiguration, CancellationToken.None).ConfigureAwait(false);
    }
}
