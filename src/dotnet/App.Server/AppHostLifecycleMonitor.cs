
namespace ActualChat.App.Server;

internal class AppHostLifecycleMonitor : IHostedLifecycleService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // ReSharper disable once ExplicitCallerInfoArgument
        Tracer.Default.Point("App.Server is starting...");
        return Task.CompletedTask;
    }
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        // ReSharper disable once ExplicitCallerInfoArgument
        Tracer.Default.Point("App.Server is started!");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        // ReSharper disable once ExplicitCallerInfoArgument
        Tracer.Default.Point("App.Server is stopping...");
        return Task.CompletedTask;
    }
    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        // ReSharper disable once ExplicitCallerInfoArgument
        Tracer.Default.Point("App.Server is stopped!");
        return Task.CompletedTask;
    }
}
