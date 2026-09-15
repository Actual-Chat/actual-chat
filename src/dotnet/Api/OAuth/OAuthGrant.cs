namespace ActualChat.OAuth;

/// <summary>
/// A user's consent to an OAuth client: one permanent OpenIddict authorization plus its backing session.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record OAuthGrant(
    [property: DataMember, Key(0)] string Id,
    [property: DataMember, Key(1)] string ClientId,
    [property: DataMember, Key(2)] string ClientName,
    [property: DataMember, Key(3)] ApiArray<string> Scopes,
    [property: DataMember, Key(4)] Moment CreatedAt,
    [property: DataMember, Key(5)] Moment LastUsedAt,
    [property: DataMember, Key(6)] Moment ExpiresAt);
