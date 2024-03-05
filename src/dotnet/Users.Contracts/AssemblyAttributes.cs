using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.OneBackendServer), ServiceMode.Mixed, Priority = 1)]
[assembly: BackendService(nameof(HostRole.UsersBackend), ServiceMode.Server)]
[assembly: BackendClient(nameof(HostRole.UsersBackend))]
