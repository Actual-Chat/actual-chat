namespace ActualChat.Security;

/// <summary>
/// A value with an expiration time for secure operations.
/// </summary>
[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(Internal.DecryptedSecureTokenMessagePackFormatter))]
public sealed partial record DecryptedSecureToken(
    [property: DataMember, Key(0)] string Value,
    [property: DataMember, Key(1)] Moment ExpiresAt
);
