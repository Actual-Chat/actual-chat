using ActualChat.Comparison;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Represents a thread conversation within a chat, grouping related entries.
/// </summary>
[ParameterComparer(typeof(ByIdAndVersionParameterComparer<ConversationId, long>))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record Conversation(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ConversationId Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long Version = 0
) : IHasId<ConversationId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly VersionEqualityComparer<Conversation, ConversationId> VersionEqualityComparer = new();
    public static readonly Requirement<Conversation> MustExist = Requirement.New(
        (Conversation? c) => c?.Id is not null,
        new(() => StandardError.NotFound<Conversation>()));

    [DataMember, MemoryPackOrder(2), Key(2)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(3), Key(3)] public string Description { get; init; } = "";
    [DataMember, MemoryPackOrder(4), Key(4)] public string Summary { get; init; } = "";
    [DataMember, MemoryPackOrder(5), Key(5)] public long EndEntryLid { get; init; } = Id.StartEntryLid;
    [DataMember, MemoryPackOrder(6), Key(6)] public Moment StartsAt { get; init; }
    [DataMember, MemoryPackOrder(7), Key(7)] public Moment EndsAt { get; init; }
    [DataMember, MemoryPackOrder(8), Key(8)] public int MessageCount { get; init; }
    [DataMember, MemoryPackOrder(9), Key(9)] public IReadOnlyList<AuthorId> AuthorIds { get; init; } = [];
    [DataMember, MemoryPackOrder(10), Key(10)] public int AttachmentCount { get; init; }
    [DataMember, MemoryPackOrder(11), Key(11)] public Symbol[] AttachmentIds { get; init; } = [];
    [DataMember, MemoryPackOrder(12), Key(12)] public ChatEntryAttachment[] Attachments { get; init; } = []; // Populated only on reads by ConversationsBackend
    [DataMember, MemoryPackOrder(13), Key(13)] public bool IsExpandedByDefault { get; init; }

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Range<long> EntryLidRange => new(Id.StartEntryLid, EndEntryLid + 1);

    // This record relies on referential equality
    public bool Equals(Conversation? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public bool VersionEquals(Conversation? other) => VersionEqualityComparer.Equals(this, other);
}

/// <summary>
/// Represents changes to a <see cref="Conversation"/> for incremental updates.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
[method:MemoryPackConstructor, SerializationConstructor]
public sealed partial record ConversationDiff() : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public string? Title { get; init; }
    [DataMember, MemoryPackOrder(1)] public string? Description { get; init; }
    [DataMember, MemoryPackOrder(2)] public string? Summary { get; init; }
    [DataMember, MemoryPackOrder(3)] public long? EndEntryLid { get; init; }
    [DataMember, MemoryPackOrder(4)] public Moment? StartsAt { get; init; }
    [DataMember, MemoryPackOrder(5)] public Moment? EndsAt { get; init; }
    [DataMember, MemoryPackOrder(6)] public int? MessageCount { get; init; }
    [DataMember, MemoryPackOrder(7)] public IReadOnlyList<AuthorId>? AuthorIds { get; init; }
    [DataMember, MemoryPackOrder(8)] public int? AttachmentCount { get; init; }
    [DataMember, MemoryPackOrder(9)] public Symbol[]? AttachmentIds { get; init; } = [];
    [DataMember, MemoryPackOrder(10)] public bool? IsExpandedByDefault { get; init; }

    public ConversationDiff(Conversation conversation) : this()
    {
        Title = conversation.Title;
        Description = conversation.Description;
        Summary = conversation.Summary;
        EndEntryLid = conversation.EndEntryLid;
        StartsAt = conversation.StartsAt;
        EndsAt = conversation.EndsAt;
        MessageCount = conversation.MessageCount;
        AuthorIds = conversation.AuthorIds;
        AttachmentCount = conversation.AttachmentCount;
        AttachmentIds = conversation.AttachmentIds;
        IsExpandedByDefault = conversation.IsExpandedByDefault;
    }
}
