using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: ServiceMode(nameof(HostRole.SingleServer), ServiceMode.Mixed, 1)]
[assembly: ServiceMode(nameof(HostRole.AudioBackend), ServiceMode.Server)]
