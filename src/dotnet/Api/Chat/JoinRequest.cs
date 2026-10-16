using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// A request to join a private chat or Place (its root chat), decided by anyone with
/// <see cref="ChatPermissions.EditMembers"/>. A chat request linked to a Place request
/// (<see cref="ParentId"/>) waits for the Place one.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record JoinRequest(
    [property: DataMember, Key(0)] JoinRequestId Id,
    [property: DataMember, Key(1)] long Version = 0
    ) : IHasId<JoinRequestId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public ChatId ChatId { get; init; } = null!;
    [DataMember, Key(3)] public UserId UserId { get; init; } = null!;
    [DataMember, Key(4)] public JoinRequestStatus Status { get; init; }
    [DataMember, Key(5)] public JoinRequestId? ParentId { get; init; }
    [DataMember, Key(6)] public Symbol InviteId { get; init; }
    [DataMember, Key(7)] public bool WasExcluded { get; init; }
    [DataMember, Key(8)] public Moment CreatedAt { get; init; }
    [DataMember, Key(9)] public Moment ExpiresAt { get; init; }
    [DataMember, Key(10)] public Moment? DecidedAt { get; init; }
    [DataMember, Key(11)] public UserId? DecidedBy { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsOpen => Status is JoinRequestStatus.Pending or JoinRequestStatus.AwaitingPlace;

    public Moment? GetRejectedUntil(Moment now)
        => Status == JoinRequestStatus.Rejected && DecidedAt is { } decidedAt
            && decidedAt + Constants.Chat.JoinRequestRejectionPause > now
            ? decidedAt + Constants.Chat.JoinRequestRejectionPause
            : null;
}

public enum JoinRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Withdrawn = 3,
    Expired = 4,
    // A chat request waiting for its Place request; it isn't shown to the chat's moderators yet
    AwaitingPlace = 5,
}

/// <summary>
/// What the landing page of a plain link shows: just the title and picture, and what its one button does.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record JoinTarget(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] JoinTargetState State)
{
    [DataMember, Key(2)] public string Title { get; init; } = "";
    [DataMember, Key(3)] public Media.Media? Picture { get; init; }
    [DataMember, Key(4)] public string PlaceTitle { get; init; } = "";
    [DataMember, Key(5)] public bool AlsoRequestsPlace { get; init; }
    [DataMember, Key(6)] public Moment? RejectedUntil { get; init; }
}

public enum JoinTargetState
{
    Member = 0,
    CanJoin = 1,
    CanRequest = 2,
    SignInToRequest = 3,
    RequestPending = 4,
    Rejected = 5,
}
