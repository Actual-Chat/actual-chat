using ActualChat.Attributes;
using ActualLab.Rpc;

namespace ActualChat;

[BackendService(nameof(HostRole.UsersBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.UsersBackend), Scheme = nameof(ShardScheme.MaintenanceBackend))]
public interface IMaintenancesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<MaintenanceMode> Get(MaintenanceKey key, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSet(MaintenancesBackend_Set command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record MaintenancesBackend_Set(
    [property: DataMember, Key(0)] MaintenanceKey Key,
    [property: DataMember, Key(1)] MaintenanceMode Mode
) : IDelegatingCommand<Unit>, IBackendCommand, IHasShardKey
{
    [DataMember, Key(2)] public string OwnerId { get; init; } = "";

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Key.ShardKey;
}
