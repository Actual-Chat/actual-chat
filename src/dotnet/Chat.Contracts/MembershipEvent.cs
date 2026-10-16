namespace ActualChat.Chat;

/// <summary>
/// A row of the membership journal: one join or leave of a chat or Place root chat,
/// or one step of a join request. Rows are appended and never changed.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record MembershipEvent(
    [property: DataMember, Key(0)] string Id,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] UserId UserId,
    [property: DataMember, Key(3)] MembershipEventKind Kind,
    [property: DataMember, Key(4)] MembershipMethod Method
) : IHasShardKey
{
    [DataMember, Key(5)] public AuthorId? AuthorId { get; init; }
    [DataMember, Key(6)] public UserId? ActorId { get; init; }
    [DataMember, Key(7)] public Symbol InviteId { get; init; }
    [DataMember, Key(8)] public JoinRequestId? RequestId { get; init; }
    [DataMember, Key(9)] public Moment CreatedAt { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

public enum MembershipEventKind
{
    Joined = 0,
    Left = 1,
    RequestSubmitted = 10,
    RequestApproved = 11,
    RequestRejected = 12,
    RequestWithdrawn = 13,
    RequestExpired = 14,
}

/// <summary>
/// How a membership change happened. An upsert that doesn't say is <see cref="System"/>;
/// <see cref="None"/> marks an author row that isn't a membership change and isn't journaled.
/// </summary>
public enum MembershipMethod
{
    None = -1,
    System = 0,
    Creator = 1,
    Self = 2,
    InviteLink = 3,
    Added = 4,
    Restored = 5,
    JoinRequest = 6,
    Excluded = 7,
    PlaceLeft = 8,
    Template = 9,
    CopyChat = 10,
    Bot = 11,
}

/// <summary>
/// What <see cref="AuthorsBackend_Upsert"/> records in the membership journal
/// besides the join or leave itself.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record MembershipChange(
    [property: DataMember, Key(0)] MembershipMethod Method,
    [property: DataMember, Key(1)] UserId? ActorId = null
)
{
    public static readonly MembershipChange System = new(MembershipMethod.System);
    public static readonly MembershipChange None = new(MembershipMethod.None);

    [DataMember, Key(2)] public Symbol InviteId { get; init; }
    [DataMember, Key(3)] public JoinRequestId? RequestId { get; init; }
}
