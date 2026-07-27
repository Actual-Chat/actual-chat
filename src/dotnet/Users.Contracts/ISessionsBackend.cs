using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Backend service for session management and authentication state.
/// </summary>
public interface ISessionsBackend : IComputeService, IBackendService
{
    // Queries
    // An invalid session throws here, and nothing invalidates that - so the error would otherwise
    // be re-tried at NonTransientErrorInvalidationDelay for as long as the client keeps asking.
    [ComputeMethod(MinCacheDuration = 10, NonTransientErrorInvalidationDelay = 120)]
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
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
// ReSharper disable once InconsistentNaming
public partial record SessionsBackend_Upsert(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session
    ) : ISessionCommand<SessionInfoFull>, IBackendCommand, INotLogged, IHasShardKey<Session>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Session ShardKey => Session;

    [DataMember, MemoryPackOrder(1), Key(1)] public string? IPAddress { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public string? Description { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public ImmutableOptionSet Options { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public Option<UserId?> UserId { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public UserIdentity? AuthenticatedIdentity { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)] public Moment? ExpiresAt { get; init; }
}
