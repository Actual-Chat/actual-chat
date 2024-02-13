using ActualChat.Attributes;
using ActualChat.Hosting;

[assembly: ServedByRole(nameof(HostRole.MediaBackendServer))] // There must be only one backend role!
[assembly: ServedByRole(nameof(HostRole.DefaultQueue))]
