using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: ServiceMode(nameof(HostRole.MediaBackend), ServiceMode.Server)]
[assembly: ShardScheme(nameof(ShardScheme.MediaBackend))]
[assembly: CommandQueue(nameof(ShardScheme.DefaultQueue))]
