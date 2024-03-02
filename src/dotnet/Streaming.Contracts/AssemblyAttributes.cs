using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.SingleServer), ServiceMode.Mixed, Priority = 1)]
[assembly: BackendService(nameof(HostRole.AudioBackend), ServiceMode.Server)]
[assembly: BackendClient(nameof(ShardScheme.None))]
