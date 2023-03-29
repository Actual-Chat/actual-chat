namespace ActualChat.UI.Blazor.App.Services;

public class AppPresenceReporter : WorkerBase
{
    public record Options
    {
        public TimeSpan AwayTimeout { get; init; } = TimeSpan.FromMinutes(3.5);
        public MomentClockSet? Clocks { get; init; }
    }

    private IServiceProvider Services { get; }

    public AppPresenceReporter(IServiceProvider services)
        => Services = services;

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        // Worker class is needed to offload services resolving from main thread
        var worker = Services.GetRequiredService<AppPresenceReporterWorker>();
        return worker.Run(cancellationToken);
    }
}
