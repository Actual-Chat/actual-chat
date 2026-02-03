using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.AudioBackend), ServiceMode.Distributed)]
[assembly: BackendShardScheme(nameof(HostRole.AudioBackend))]
