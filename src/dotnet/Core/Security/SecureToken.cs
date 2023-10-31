using System.Diagnostics.CodeAnalysis;
using MemoryPack;

namespace ActualChat.Security;

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
