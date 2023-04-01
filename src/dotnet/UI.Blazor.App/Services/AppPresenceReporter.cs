namespace ActualChat.UI.Blazor.App.Services;

public class AppPresenceReporter : WorkerBase
{
    public record Options
    {
        public TimeSpan StartDelay { get; init; } = TimeSpan.FromSeconds(10);
        public TimeSpan AwayTimeout { get; init; } = TimeSpan.FromMinutes(3.5);
    }

    private IServiceProvider Services { get; }
    private Options Settings { get; }

    public AppPresenceReporter(Options settings, IServiceProvider services)
    {
        Services = services;
        Settings = settings;
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await Services.Clocks().CpuClock.Delay(Settings.StartDelay, cancellationToken).ConfigureAwait(false);

        // Worker class is needed to offload services resolving from main thread
        var worker = Services.GetRequiredService<AppPresenceReporterWorker>();
        await worker.Run(cancellationToken).ConfigureAwait(false);
    }
}
