using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Backend service for session management and authentication state.
/// </summary>
public interface ISessionsBackend : IComputeService, IBackendService
{
    // Queries
    [ComputeMethod(MinCacheDuration = 10)]
    Task<SessionInfoFull?> Get(Session session, CancellationToken cancellationToken = default);

    // Non-compute methods
    Task UpdateLastSeenAt(
        Session session, string? description, string? ipAddress,
        CancellationToken cancellationToken = default);

    // Commands
    [CommandHandler]
    Task<SessionInfoFull> OnUpsert(SessionsBackend_Upsert command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Command to create or update a session.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: ConstructorShape, JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
// ReSharper disable once InconsistentNaming
public partial record SessionsBackend_Upsert(
    [property: DataMember, MemoryPackOrder(0), NbKey(0)] Session Session
    ) : ISessionCommand<SessionInfoFull>, IBackendCommand, INotLogged, IHasShardKey<Session>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Session ShardKey => Session;

    [DataMember, MemoryPackOrder(1), NbKey(1)] public string? IPAddress { get; init; }
    [DataMember, MemoryPackOrder(2), NbKey(2)] public string? Description { get; init; }
    [DataMember, MemoryPackOrder(3), NbKey(3)] public ImmutableOptionSet Options { get; init; }
    [DataMember, MemoryPackOrder(4), NbKey(4)] public Option<UserId?> UserId { get; init; }
    [DataMember, MemoryPackOrder(5), NbKey(5)] public UserIdentity? AuthenticatedIdentity { get; init; }
    [DataMember, MemoryPackOrder(6), NbKey(6)] public Moment? ExpiresAt { get; init; }
}
