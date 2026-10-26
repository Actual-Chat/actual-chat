namespace ActualChat.Users;

/// <summary>
/// The id the analytics SDK on one of the user's devices goes by: Firebase's <c>app_instance_id</c> in the app,
/// GA4's <c>client_id</c> on the web. A milestone sent from the server is attributed through it.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserAnalyticsClient(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] AnalyticsClientKind Kind,
    [property: DataMember, Key(2)] string ClientId,
    [property: DataMember, Key(3)] Moment LastSeenAt
);
