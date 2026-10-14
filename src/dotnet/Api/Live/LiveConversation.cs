using ActualLab.Versioning;

namespace ActualChat.Live;

/// <summary>
/// One live conversation of a chat: its boundaries and its summary. The open one is a part of
/// <see cref="LiveSessionState"/>; a closed one is kept for a while after the session ends, until the
/// saved <see cref="Conversation"/> that covers it takes over.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record LiveConversation : IHasId<ConversationId>, IHasVersion<long>
{
    // Chat + the first entry of the block, set at the start
    [DataMember(Order = 0), Key(0)]
    public ConversationId Id { get; init; } = null!;
    // The first entry the summarizer reads, and the start of the saved conversation. 0 until it is set.
    [DataMember(Order = 1), Key(1)]
    public long ContextStartLid { get; init; }
    // End (exclusive) of what the latest summary covers; equals StartLid until the first summary
    [DataMember(Order = 2), Key(2)]
    public long SummaryEndLid { get; init; }
    // End (exclusive) of the conversation; long.MaxValue while it is open
    [DataMember(Order = 3), Key(3)]
    public long EndLid { get; init; } = long.MaxValue;
    [DataMember(Order = 4), Key(4)]
    public Moment StartedAt { get; init; }
    // Moment.MaxValue while the conversation is open
    [DataMember(Order = 5), Key(5)]
    public Moment EndedAt { get; init; } = Moment.MaxValue;
    [DataMember(Order = 6), Key(6)]
    public Moment SummarizedAt { get; init; }
    [DataMember(Order = 7), Key(7)]
    public string Title { get; init; } = "";
    [DataMember(Order = 8), Key(8)]
    public string Description { get; init; } = "";
    [DataMember(Order = 9), Key(9)]
    public string Summary { get; init; } = "";
    [DataMember(Order = 10), Key(10)]
    public int SummarizedEntryCount { get; init; }
    [DataMember(Order = 11), Key(11)]
    public IReadOnlyList<AuthorId> AuthorIds { get; init; } = [];
    [DataMember(Order = 12), Key(12)]
    public bool IsExpandedByDefault { get; init; }
    // Set for a call and nothing else
    [DataMember(Order = 13), Key(13)]
    public AuthorId? CallerId { get; init; }
    [DataMember(Order = 14), Key(14)]
    public long Version { get; init; }
    // Everyone who joined it in any way (listening, watching, recording), in order of arrival, and stays
    // listed after leaving. AuthorIds is who spoke.
    [DataMember(Order = 15), Key(15)]
    public IReadOnlyList<AuthorId> ParticipantIds { get; init; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long StartLid => Id.StartEntryLid;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsOpen => EndLid == long.MaxValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsCall => CallerId is not null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool HasSummary => SummarizedAt.EpochOffsetTicks > 0;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Range<long> LidRange => new(StartLid, EndLid);
    // The id of the saved conversation: it starts at the context start, which can precede the block
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ConversationId MaterializedId => ConversationId.New(ChatId, MaterializedStartLid);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long MaterializedStartLid => ContextStartLid > 0 ? ContextStartLid : StartLid;
    // The last entry the latest summary covers (inclusive), never before the block's first entry
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long SummaryLastLid => Math.Max(SummaryEndLid - 1, StartLid);

    public bool IsParticipant(AuthorId? authorId)
        => authorId != null && ParticipantIds.Contains(authorId);

    public Conversation ToConversation()
        => new(Id, Version) {
            Title = Title,
            Description = Description,
            Summary = Summary,
            EndEntryLid = IsOpen ? SummaryLastLid : Math.Max(EndLid - 1, StartLid),
            StartsAt = StartedAt,
            EndsAt = GetEndsAt(),
            MessageCount = SummarizedEntryCount,
            AuthorIds = AuthorIds,
            IsExpandedByDefault = IsExpandedByDefault,
            CallerId = CallerId,
        };

    public Conversation ToMaterializedConversation()
        => new(MaterializedId, Version) {
            Title = Title,
            Description = Description,
            Summary = Summary,
            EndEntryLid = IsOpen ? SummaryEndLid - 1 : EndLid - 1,
            StartsAt = StartedAt,
            EndsAt = GetEndsAt(),
            MessageCount = SummarizedEntryCount,
            AuthorIds = AuthorIds,
            IsExpandedByDefault = IsExpandedByDefault,
            CallerId = CallerId,
        };

    // Compares by content, Version excluded: a write that doesn't touch the card still bumps it, and a
    // compute method that rebuilds the record on every recompute needs this to consolidate the churn.
    public bool Equals(LiveConversation? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null)
            return false;

        return Id == other.Id
            && ContextStartLid == other.ContextStartLid
            && SummaryEndLid == other.SummaryEndLid
            && EndLid == other.EndLid
            && StartedAt == other.StartedAt
            && EndedAt == other.EndedAt
            && SummarizedAt == other.SummarizedAt
            && SummarizedEntryCount == other.SummarizedEntryCount
            && IsExpandedByDefault == other.IsExpandedByDefault
            && CallerId == other.CallerId
            && Title == other.Title
            && Description == other.Description
            && Summary == other.Summary
            && AuthorIds.SequenceEqual(other.AuthorIds)
            && ParticipantIds.SequenceEqual(other.ParticipantIds);
    }

    public override int GetHashCode()
        => HashCode.Combine(Id, SummaryEndLid, EndLid, SummarizedAt, SummarizedEntryCount, AuthorIds.Count, ParticipantIds.Count);

    private Moment GetEndsAt()
        => !IsOpen ? EndedAt : SummarizedAt == default ? StartedAt : SummarizedAt;
}
