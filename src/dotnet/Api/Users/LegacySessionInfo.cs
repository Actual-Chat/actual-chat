using ActualLab.Versioning;

namespace ActualChat.Users;

/// <summary>
/// Legacy session info format for backward compatibility with older clients.
/// </summary>
[Obsolete("Use SessionInfo instead.")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
public partial record LegacySessionInfo() : IRequirementTarget, IHasVersion<long>
{
    // From SessionAuthInfo
    [DataMember(Order = 0), MemoryPackOrder(0)] public string SessionHash { get => field ?? ""; init; } = "";
    [DataMember(Order = 1), MemoryPackOrder(1)] public UserIdentity AuthenticatedIdentity { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), StringAsSymbolMemoryPackFormatter] public string UserId { get; init; } = "";
    [DataMember(Order = 3), MemoryPackOrder(3)] public bool IsSignOutForced { get; init; }

    // SessionInfo-specific properties
    [DataMember(Order = 10), MemoryPackOrder(10)] public long Version { get; init; }
    [DataMember(Order = 11), MemoryPackOrder(11)] public Moment CreatedAt { get; init; }
    [DataMember(Order = 12), MemoryPackOrder(12)] public Moment LastSeenAt { get; init; }
    [DataMember(Order = 13), MemoryPackOrder(13)] public string IPAddress { get => field ?? ""; init; } = "";
    [DataMember(Order = 14), MemoryPackOrder(14)] public string UserAgent { get => field ?? ""; init; } = "";
    [DataMember(Order = 15), MemoryPackOrder(15)] public ImmutableOptionSet Options { get; init; }

    public static LegacySessionInfo From(SessionInfoFull sessionInfo)
        => new() {
            SessionHash = sessionInfo.Session.Hash,
            AuthenticatedIdentity = sessionInfo.AuthenticatedIdentity,
            UserId = sessionInfo.UserId?.Value ?? "",
            IsSignOutForced = !sessionInfo.IsActive,
            Version = sessionInfo.Version,
            CreatedAt = sessionInfo.CreatedAt,
            LastSeenAt = sessionInfo.LastSeenAt,
            IPAddress = sessionInfo.IPAddress,
            UserAgent = sessionInfo.Description,
            Options = sessionInfo.Options,
        };
}
