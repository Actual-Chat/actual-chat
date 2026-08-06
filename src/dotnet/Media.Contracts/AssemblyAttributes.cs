using ActualChat.Attributes;
using ActualChat.Sharding;

// [assembly: BackendService(nameof(HostRole.OneServer), ServiceMode.Local, Priority = 1)]
[assembly: BackendService(nameof(HostRole.MediaBackend), ServiceMode.Server)]
[assembly: BackendShardScheme(nameof(ShardScheme.MediaBackend))]
