using Stl.Fusion.Operations.Internal;

namespace ActualChat.Jobs;

public class JobScheduler : IOperationCompletionListener
{
    private LocalJobQueue JobQueue { get; }

    public JobScheduler(LocalJobQueue jobQueue)
        => JobQueue = jobQueue;

    public bool IsReady()
        => true;

    public async Task OnOperationCompleted(IOperation operation, CommandContext? commandContext)
    {
        if (operation is not TransientOperation)
            return;

        var jobConfigurations = operation.Items.Items.Values
            .OfType<IJobConfiguration>();

        foreach (var jobConfiguration in jobConfigurations)
            // TODO(AK): it's suspicious the we don't have CancellationToken there
            await JobQueue.Enqueue(jobConfiguration, CancellationToken.None).ConfigureAwait(false);
    }
}
