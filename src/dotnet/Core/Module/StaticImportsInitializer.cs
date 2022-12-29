using ActualChat.Hosting;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Module;

public class StaticImportsInitializer : IHostedService
{
    public StaticImportsInitializer(IServiceProvider services)
    {
        var hostInfo = services.GetRequiredService<HostInfo>();
        if (hostInfo.RequiredServiceScopes.Contains(ServiceScope.Test))
            return; // Don't set DefaultLog for tests

        if (DefaultLog == NullLogger.Instance)
            DefaultLog = services.LogFor("ActualChat.Unknown");
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
