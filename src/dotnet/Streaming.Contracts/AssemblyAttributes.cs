using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.AudioBackend), ServiceMode.Hybrid)]
[assembly: BackendClient(nameof(ShardScheme.None))]
