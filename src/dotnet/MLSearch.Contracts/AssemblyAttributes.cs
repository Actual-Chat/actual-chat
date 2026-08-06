using ActualChat.Attributes;

[assembly: BackendService(nameof(HostRole.OneServer), ServiceMode.Local, Priority = 1)]
[assembly: BackendService(nameof(HostRole.SearchBackend), ServiceMode.Server)]
[assembly: BackendShardScheme(nameof(HostRole.SearchBackend))]
