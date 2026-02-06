using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Users;

/// <summary>
/// Backend service for session management and authentication state.
/// </summary>
public interface ISessionsBackend : IComputeService, IBackendService
{
    // Queries
    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionInfo?> Get(Session session, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<bool> IsSignOutForced(Session session, CancellationToken cancellationToken = default);

    // Non-compute methods
    Task UpdatePresence(Session session, CancellationToken cancellationToken = default);

    // Commands
    [CommandHandler]
    Task<SessionInfo> OnUpsert(SessionsBackend_Upsert command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnSignOut(SessionsBackend_SignOut command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Command to create or update a session.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
// ReSharper disable once InconsistentNaming
public partial record SessionsBackend_Upsert(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string IPAddress,
    [property: DataMember, MemoryPackOrder(2)] string UserAgent,
    [property: DataMember, MemoryPackOrder(3)] ImmutableOptionSet Options,
    [property: DataMember, MemoryPackOrder(4)] string? UserId = null,
    [property: DataMember, MemoryPackOrder(5)] string? AuthenticatedIdentity = null
) : ISessionCommand<SessionInfo>, IBackendCommand, INotLogged, IHasShardKey<Session>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Session ShardKey => Session;

    public SessionsBackend_Upsert(Session session, string ipAddress = "", string userAgent = "")
        : this(session, ipAddress, userAgent, default) { }
}

/// <summary>
/// Command to sign out a session.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record SessionsBackend_SignOut(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] bool Force = false
) : ISessionCommand<Unit>, IBackendCommand, IHasShardKey<Session>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Session ShardKey => Session;
}
