using MemoryPack;

namespace ActualChat.Security;

/// <summary>
/// A time-limited token used for secure operations.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record SecureToken(
    [property: DataMember, MemoryPackOrder(0)] string Token,
    [property: DataMember, MemoryPackOrder(1)] Moment ExpiresAt
) {
    public static readonly TimeSpan Lifespan = TimeSpan.FromMinutes(24*60);
    public static readonly string Prefix = "! "; // Must contain space!

    public static bool HasValidPrefix([NotNullWhen(true)] string? token)
        => token.OrdinalStartsWith(Prefix);
}
