using ActualChat.Attributes;

[assembly: BackendService(nameof(HostRole.OneServer), ServiceMode.Local, Priority = 1)]
[assembly: BackendService(nameof(HostRole.UsersBackend), ServiceMode.Server)] // TBD: -> Distributed
[assembly: BackendShardScheme(nameof(HostRole.UsersBackend))]
