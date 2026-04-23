using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat.Users;

[BackendService(nameof(HostRole.UsersBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.UsersBackend))]
public interface ISessionTemporalsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<string?> Get(Session session, string key, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSet(SessionTemporalsBackend_Set command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SessionTemporalsBackend_Set(
    [property: DataMember, MemoryPackOrder(0), NbKey(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), NbKey(1)] string Key,
    [property: DataMember, MemoryPackOrder(2), NbKey(2)] string? Value
) : ICommand<Unit>, IBackendCommand, IHasShardKey<Session>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Session ShardKey => Session;
}
