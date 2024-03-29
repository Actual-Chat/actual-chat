using ActualChat.Attributes;
using ActualChat.Hosting;

// [assembly: BackendService(nameof(HostRole.OneServer), ServiceMode.Local, Priority = 1)]
[assembly: BackendService(nameof(HostRole.MediaBackend), ServiceMode.Hybrid)]
[assembly: BackendClient(nameof(ShardScheme.MediaBackend))]
