namespace ActualChat.App.Wasm.Diagnostics;

public class TaskMonitor : WorkerBase
{
    private IServiceProvider Services { get; }
    private ILogger Log { get; }

    public TaskMonitor(IServiceProvider services) : base()
    {
        Services = services;
        Log = services.LogFor(GetType());
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var scheduler = TaskScheduler.Current;
        var tScheduler = scheduler.GetType();
        var mGetTasks = tScheduler.GetMethod("GetScheduledTasksForDebugger",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)!;
        Log.LogInformation("Scheduler type: {SchedulerType}", tScheduler.GetName(true, true));
        while (!cancellationToken.IsCancellationRequested) {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            try {
                var tasks = mGetTasks.Invoke(scheduler, null) as Task[];
                if (tasks == null || tasks.Length < 5)
                    return;

                var sTasks = tasks.ToDelimitedString("\r\n");
                Log.LogInformation("Tasks:\r\n{Tasks}", sTasks);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to get scheduled tasks");
            }
        }
    }
}
