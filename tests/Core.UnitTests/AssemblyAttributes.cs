using ActualChat.Attributes;

[assembly: BackendService(nameof(HostRole.TestBackend), ServiceMode.Server)]
[assembly: BackendShardScheme(nameof(HostRole.TestBackend))]
