using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.AudioBackend), ServiceMode.RoutingServer)]
[assembly: BackendClient(nameof(ShardScheme.None))]
