using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.SearchBackend), ServiceMode.Hybrid)]
[assembly: BackendClient(nameof(HostRole.SearchBackend))]
