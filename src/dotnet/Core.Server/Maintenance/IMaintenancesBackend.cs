using ActualChat.Attributes;
using ActualLab.Rpc;

namespace ActualChat;

[BackendService(nameof(HostRole.UsersBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.UsersBackend), Scheme = nameof(ShardScheme.MaintenanceBackend))]
public interface IMaintenancesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<MaintenanceMode> GetMode(MaintenanceKey key, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Maintenance> Get(MaintenanceKey key, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSet(MaintenancesBackend_Set command, CancellationToken cancellationToken);
}

/// <summary>
/// A maintenance state: its <see cref="Mode"/> plus the <see cref="OwnerId"/> of the operation
/// that owns it, which only that operation may change or clear. A Place keeps all of its chats'
/// maintenance on its root key: empty <see cref="Targets"/> cover the whole Place, otherwise
/// only the listed chats are affected.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record Maintenance(
    [property: DataMember, Key(0)] MaintenanceMode Mode,
    [property: DataMember, Key(1)] string OwnerId)
{
    public static readonly Maintenance None = new(MaintenanceMode.None, "");

    [DataMember, Key(2)] public UserId? StartedBy { get; init; }
    [DataMember, Key(3)] public Moment StartedAt { get; init; }
    [DataMember, Key(4)] public ApiArray<string> Targets { get; init; }

    public Maintenance() : this(MaintenanceMode.None, "") { }

    public MaintenanceMode GetMode(string? target)
        => target is null || Targets.IsEmpty || Targets.Contains(target)
            ? Mode
            : MaintenanceMode.None;
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record MaintenancesBackend_Set(
    [property: DataMember, Key(0)] MaintenanceKey Key,
    [property: DataMember, Key(1)] MaintenanceMode Mode
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [DataMember, Key(2)] public string OwnerId { get; init; } = "";
    [DataMember, Key(3)] public UserId? StartedBy { get; init; }
    // Recorded only when the maintenance starts; the handler's clock is used when it's absent
    [DataMember, Key(4)] public Moment? StartedAt { get; init; }
    [DataMember, Key(5)] public ApiArray<string> Targets { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Key.ShardKey;
}
