
namespace ActualChat.App.Server;

internal class AppHostLificycleMonitor : IHostedLifecycleService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        Tracer.Default.Point("App.Server is starting...");
        return Task.CompletedTask;
    }
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        Tracer.Default.Point("App.Server is started!");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        Tracer.Default.Point("App.Server is stopping...");
        return Task.CompletedTask;
    }
    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        Tracer.Default.Point("App.Server is stopped!");
        return Task.CompletedTask;
    }
}
