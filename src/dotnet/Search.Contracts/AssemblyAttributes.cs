using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.SearchBackend), ServiceMode.RoutingServer)]
[assembly: BackendClient(nameof(HostRole.SearchBackend))]
