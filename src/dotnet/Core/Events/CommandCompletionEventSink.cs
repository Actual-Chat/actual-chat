using Stl.Fusion.Operations.Internal;

namespace ActualChat.Events;

public class CommandCompletionEventSink : IOperationCompletionListener
{
    private LocalEventQueue EventQueue { get; }

    public CommandCompletionEventSink(LocalEventQueue eventQueue)
        => EventQueue = eventQueue;

    public bool IsReady()
        => true;

    public async Task OnOperationCompleted(IOperation operation, CommandContext? commandContext)
    {
        if (operation is not TransientOperation)
            return;

        var jobConfigurations = operation.Items.Items.Values
            .OfType<IEventConfiguration>();

        foreach (var jobConfiguration in jobConfigurations)
            // TODO(AK): it's suspicious the we don't have CancellationToken there
            await EventQueue.Enqueue(jobConfiguration, CancellationToken.None).ConfigureAwait(false);
    }
}
