using ActualChat.Hosting;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Module;

public class StaticImportsInitializer : IHostedService
{
    public StaticImportsInitializer(IServiceProvider services)
    {
        var hostInfo = services.HostInfo();
        if (hostInfo.HostKind.IsServer() && hostInfo.IsTested)
            return; // Don't set DefaultLog for test server

        if (DefaultLog == NullLogger.Instance)
            DefaultLog = services.LogFor("ActualChat.Unknown");
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
