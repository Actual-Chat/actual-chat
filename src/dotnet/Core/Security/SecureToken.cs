namespace ActualChat.Security;

/// <summary>
/// A time-limited token used for secure operations.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[MessagePackFormatter(typeof(Internal.SecureTokenMessagePackFormatter))]
public sealed partial record SecureToken(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string Token,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Moment ExpiresAt
) {
    public static readonly TimeSpan Lifespan = TimeSpan.FromMinutes(30);
    public static readonly string Prefix = "! "; // Must contain space!

    public static bool HasValidPrefix([NotNullWhen(true)] string? token)
        => (token ?? "").StartsWith(Prefix);
}
