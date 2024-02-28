using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.SingleServer), ServiceMode.Mixed, Priority = 1)]
[assembly: BackendService(nameof(HostRole.SearchBackend), ServiceMode.Server)]
[assembly: BackendClient(nameof(HostRole.SearchBackend))]
