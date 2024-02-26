using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: ServiceMode(nameof(HostRole.SingleServer), ServiceMode.Local, Priority = 1)]
[assembly: ServiceMode(nameof(HostRole.MediaBackend), ServiceMode.Server)]
[assembly: ShardScheme(nameof(ShardScheme.MediaBackend))]
[assembly: CommandQueue(nameof(ShardScheme.DefaultQueue))]
