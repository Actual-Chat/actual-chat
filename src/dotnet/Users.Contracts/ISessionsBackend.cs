using ActualLab.Rpc;
using System.Text;

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
[DataContract, MessagePackObject]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
// ReSharper disable once InconsistentNaming
public partial record SessionsBackend_Upsert(
    [property: DataMember, Key(0)] Session Session
    ) : ISessionCommand<SessionInfoFull>, IBackendCommand, ISanitized, IHasShardKey<Session>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Session ShardKey => Session;

    [DataMember, Key(1)] public string? IPAddress { get; init; }
    [DataMember, Key(2)] public string? Description { get; init; }
    // Order/Key 3 reserved (was Options, which no caller ever set) — do not reuse.
    [DataMember, Key(4)] public Option<UserId?> UserId { get; init; }
    [DataMember, Key(5)] public UserIdentity? AuthenticatedIdentity { get; init; }
    [DataMember, Key(6)] public Moment? ExpiresAt { get; init; }

    // Protected methods

    protected virtual bool PrintMembers(StringBuilder builder)
    {
        // Session redacts itself, and an identity's value is the provider's secret -
        // only its schema is worth a log line
        builder.Append("Session = ").Append(Session)
            .Append(", IPAddress = ").Append(IPAddress)
            .Append(", Description = ").Append(Description)
            .Append(", UserId = ").Append(UserId)
            .Append(", AuthenticatedIdentity = ").Append(AuthenticatedIdentity?.Schema)
            .Append(", ExpiresAt = ").Append(ExpiresAt);
        return true;
    }
}
