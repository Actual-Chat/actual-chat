using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.SingleServer), ServiceMode.Local, Priority = 1)]
[assembly: BackendService(nameof(HostRole.MediaBackend), ServiceMode.Server)]
[assembly: BackendClient(nameof(HostRole.MediaBackend))]
