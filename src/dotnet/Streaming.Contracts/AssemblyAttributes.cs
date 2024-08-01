using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.AudioBackend), ServiceMode.Distributed)]
[assembly: BackendClient(nameof(ShardScheme.None))]
