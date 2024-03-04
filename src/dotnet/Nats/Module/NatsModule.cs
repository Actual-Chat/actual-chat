using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using NATS.Client.Core;
using NATS.Client.Hosting;

namespace ActualChat.Nats.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class NatsModule(IServiceProvider moduleServices)
    : HostModule<NatsSettings>(moduleServices), IServerModule
{
    public void AddNatsQueues(IServiceCollection services)
    {
        var natsTimeout = IsDevelopmentInstance
            ? 300
            : 10;
        services.AddNats(
            poolSize: 1,
            opts => opts with {
                // AuthOpts =
                // Url = "ws://localhost:8222",
                TlsOpts = new NatsTlsOpts { Mode = TlsMode.Auto },
                CommandTimeout = TimeSpan.FromSeconds(natsTimeout),
                ConnectTimeout = TimeSpan.FromSeconds(natsTimeout),
                RequestTimeout = TimeSpan.FromSeconds(natsTimeout),
            });
    }
}
