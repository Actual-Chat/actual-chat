using System.Security;
using MemoryPack;

namespace ActualChat.Users;

/// <summary>
/// Legacy session authentication info - use <see cref="SessionInfo"/> instead.
/// </summary>
[Obsolete("2025.01: Use SessionInfo instead.")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record LegacySessionAuthInfo : IRequirementTarget
{
    public static Requirement<LegacySessionAuthInfo> MustBeAuthenticated { get; set; } = Requirement.New(
        (LegacySessionAuthInfo? i) => i?.IsAuthenticated() ?? false,
        new("Session is not authenticated.", m => new SecurityException(m)));

    [DataMember(Order = 0), MemoryPackOrder(0)] public string SessionHash { get => field ?? ""; init; }

    // Authentication

    [DataMember(Order = 1), MemoryPackOrder(1)]
    public UserIdentity AuthenticatedIdentity { get; init; }

    [DataMember(Order = 2), MemoryPackOrder(2)]
    public string UserId { get; init; } = "";

    [DataMember(Order = 3), MemoryPackOrder(3)]
    public bool IsSignOutForced { get; init; }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public LegacySessionAuthInfo()
        => SessionHash = "";
    public LegacySessionAuthInfo(Session? session)
        => SessionHash = session?.Hash ?? "";

    public bool IsAuthenticated()
        => !(IsSignOutForced || UserId.IsNullOrEmpty());
}
