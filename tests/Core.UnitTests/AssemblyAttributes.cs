using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.TestBackend), ServiceMode.Server)]
[assembly: BackendClient(nameof(HostRole.TestBackend))]
