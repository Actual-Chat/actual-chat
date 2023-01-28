using System.Text;

namespace ActualChat.App.Wasm.Diagnostics;

public class TaskMonitor : WorkerBase
{
    private const int CheckPeriod = 100;
    private const int DelayLimit = 50;

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

        var sb = new StringBuilder();
        var stopwatch = new Stopwatch();
        while (!cancellationToken.IsCancellationRequested) {
            stopwatch.Reset();
            await Task.Delay(CheckPeriod, cancellationToken).ConfigureAwait(false);
            var delay = stopwatch.ElapsedMilliseconds - CheckPeriod;
            if (delay < DelayLimit)
                continue;

            try {
                var tasks = mGetTasks.Invoke(scheduler, null) as Task[];
                sb.Append("Delay: ");
                sb.Append(delay);
                sb.Append("ms, task queue:");
                if (tasks == null || tasks.Length == 0)
                    sb.Append(" none");
                else {
                    foreach (var task in tasks) {
                        sb.Append("\r\n -");
                        sb.Append(task);
                    }
                }
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                Log.LogWarning(sb.ToString());
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to get scheduled tasks");
            }
            finally {
                sb.Clear();
            }
        }
    }
}
