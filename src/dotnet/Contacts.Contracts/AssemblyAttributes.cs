using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: BackendService(nameof(HostRole.OneServer), ServiceMode.Local, Priority = 1)]
[assembly: BackendService(nameof(HostRole.ContactsBackend), ServiceMode.Server)] // TBD: -> Distributed
[assembly: BackendClient(nameof(HostRole.ContactsBackend))]
