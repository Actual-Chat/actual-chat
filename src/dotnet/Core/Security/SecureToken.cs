namespace ActualChat.Security;

/// <summary>
/// A time-limited token used for secure operations.
/// </summary>
[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(Internal.SecureTokenMessagePackFormatter))]
public sealed partial record SecureToken(
    [property: DataMember, Key(0)] string Token,
    [property: DataMember, Key(1)] Moment ExpiresAt
) {
    public static readonly TimeSpan Lifespan = TimeSpan.FromMinutes(30);
    public static readonly string Prefix = "! "; // Must contain space!

    public static bool HasValidPrefix([NotNullWhen(true)] string? token)
        => (token ?? "").StartsWith(Prefix);
}
