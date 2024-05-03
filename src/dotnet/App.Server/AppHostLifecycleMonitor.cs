
namespace ActualChat.App.Server;

internal sealed class AppHostLifecycleMonitor(IServiceProvider services) : IHostedLifecycleService
{
    private Tracer? _tracer;

    private Tracer Tracer => _tracer ??= services.Tracer(typeof(AppHostLifecycleMonitor));

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // ReSharper disable once ExplicitCallerInfoArgument
        Tracer.Point("[!] App.Server is starting...");
        return Task.CompletedTask;
    }
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        // ReSharper disable once ExplicitCallerInfoArgument
        Tracer.Point("[!] App.Server is started.");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        // ReSharper disable once ExplicitCallerInfoArgument
        Tracer.Point("[!] App.Server is stopping...");
        return Task.CompletedTask;
    }
    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        // ReSharper disable once ExplicitCallerInfoArgument
        Tracer.Point("[!] App.Server is stopped.");
        return Task.CompletedTask;
    }
}
