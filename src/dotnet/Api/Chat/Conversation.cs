using ActualChat.Comparison;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Represents a thread conversation within a chat, grouping related entries.
/// </summary>
[ParameterComparer(typeof(ByIdAndVersionParameterComparer<ConversationId, long>))]
[DataContract, MessagePackObject]
public sealed partial record Conversation(
    [property: DataMember, Key(0)] ConversationId Id,
    [property: DataMember, Key(1)] long Version = 0
) : IHasId<ConversationId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly VersionEqualityComparer<Conversation, ConversationId> VersionEqualityComparer = new();
    public static readonly Requirement<Conversation> MustExist = Requirement.New(
        (Conversation? c) => c?.Id is not null,
        new(() => StandardError.NotFound<Conversation>()));

    [DataMember, Key(2)] public string Title { get; init; } = "";
    [DataMember, Key(3)] public string Description { get; init; } = "";
    [DataMember, Key(4)] public string Summary { get; init; } = "";
    [DataMember, Key(5)] public long EndEntryLid { get; init; } = Id.StartEntryLid;
    [DataMember, Key(6)] public Moment StartsAt { get; init; }
    [DataMember, Key(7)] public Moment EndsAt { get; init; }
    [DataMember, Key(8)] public int MessageCount { get; init; }
    [DataMember, Key(9)] public IReadOnlyList<AuthorId> AuthorIds { get; init; } = [];
    [DataMember, Key(10)] public int AttachmentCount { get; init; }
    [DataMember, Key(11)] public Symbol[] AttachmentIds { get; init; } = [];
    [DataMember, Key(12)] public ChatEntryAttachment[] Attachments { get; init; } = []; // Populated only on reads by ConversationsBackend
    [DataMember, Key(13)] public bool IsExpandedByDefault { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Range<long> EntryLidRange => new(Id.StartEntryLid, EndEntryLid + 1);

    // This record relies on referential equality
    public bool Equals(Conversation? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public bool VersionEquals(Conversation? other) => VersionEqualityComparer.Equals(this, other);
}

/// <summary>
/// Represents changes to a <see cref="Conversation"/> for incremental updates.
/// </summary>
[DataContract, MessagePackObject(true)]
[method:SerializationConstructor]
public sealed partial record ConversationDiff() : RecordDiff
{
    [DataMember] public string? Title { get; init; }
    [DataMember] public string? Description { get; init; }
    [DataMember] public string? Summary { get; init; }
    [DataMember] public long? EndEntryLid { get; init; }
    [DataMember] public Moment? StartsAt { get; init; }
    [DataMember] public Moment? EndsAt { get; init; }
    [DataMember] public int? MessageCount { get; init; }
    [DataMember] public IReadOnlyList<AuthorId>? AuthorIds { get; init; }
    [DataMember] public int? AttachmentCount { get; init; }
    [DataMember] public Symbol[]? AttachmentIds { get; init; } = [];
    [DataMember] public bool? IsExpandedByDefault { get; init; }

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
