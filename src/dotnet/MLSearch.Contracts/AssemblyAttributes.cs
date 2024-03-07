using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.OneBackendServer), ServiceMode.Mixed, Priority = 1)]
[assembly: BackendService(nameof(HostRole.MLSearchIndexing), ServiceMode.Server)]
[assembly: BackendClient(nameof(HostRole.MLSearchIndexing))]
