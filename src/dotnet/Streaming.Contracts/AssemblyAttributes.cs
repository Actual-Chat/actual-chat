using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Sharding;

[assembly: BackendService(nameof(HostRole.AudioBackend), ServiceMode.Distributed)]
[assembly: BackendClient(nameof(ShardScheme.None))]
