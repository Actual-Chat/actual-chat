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

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record SessionTemporalsBackend_Set(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] string Key,
    [property: DataMember, Key(2)] string? Value
) : ICommand<Unit>, IBackendCommand, IHasShardKey<Session>
{
    [IgnoreDataMember, IgnoreMember]
    public Session ShardKey => Session;
}
