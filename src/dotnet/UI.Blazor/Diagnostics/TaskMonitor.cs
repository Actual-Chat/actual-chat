using System.Text;

namespace ActualChat.UI.Blazor.Diagnostics;

public class TaskMonitor : WorkerBase
{
    private IServiceProvider Services { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public TimeSpan CheckInPeriod { get; init; } = TimeSpan.FromMilliseconds(20);
    public TimeSpan DumpPeriod { get; init; } = TimeSpan.FromSeconds(1000);

    public TaskMonitor(IServiceProvider services) : base()
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var scheduler = TaskScheduler.Current;
        var tScheduler = scheduler.GetType();
        var mGetTasks = tScheduler.GetMethod("GetScheduledTasksForDebugger",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)!;
        Log.LogInformation("Scheduler type: {SchedulerType}", tScheduler.GetName(true, true));

        var sb = new StringBuilder();
        var lastDumpTime = Clocks.CpuClock.Now;
        while (!cancellationToken.IsCancellationRequested) {
            await Task.Delay(CheckInPeriod, cancellationToken).ConfigureAwait(false);
            var now = Clocks.CpuClock.Now;
            if (lastDumpTime + DumpPeriod > now)
                continue;

            lastDumpTime = now;
            try {
                var tasks = mGetTasks.Invoke(scheduler, null) as Task[];
                if (tasks == null || tasks.Length == 0)
                    continue;
                sb.Append("Task queue:");
                foreach (var task in tasks) {
                    sb.Append("\r\n- ");
                    sb.Append(task);
                }
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                Log.LogInformation(sb.ToString());
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
