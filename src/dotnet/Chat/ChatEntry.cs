using ActualChat.Comparison;
using MemoryPack;
using Stl.Fusion.Blazor;
using Stl.Versioning;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatEntry(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<ChatEntryId>, IHasVersion<long>, IRequirementTarget
{
    public static IdAndVersionEqualityComparer<ChatEntry, ChatEntryId> EqualityComparer { get; } = new();
    public static ChatEntry Loading { get; } = new(default, -1); // Should differ by Id & Version from None

    public static Requirement<ChatEntry> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<ChatEntry>()),
        (ChatEntry? c) => c is { Id.IsNone: false });
    public static Requirement<ChatEntry> MustNotBeRemoved { get; } = Requirement.New(
        new(() => StandardError.NotFound<ChatEntry>()),
        (ChatEntry? c) => c is { Id.IsNone: false, IsRemoved: false });

    public static ChatEntry Removed(ChatEntryId id)
        => new (id) { IsRemoved = true };

    [DataMember, MemoryPackOrder(10)] public bool IsRemoved { get; init; }
    [DataMember, MemoryPackOrder(11)] public AuthorId AuthorId { get; init; }
    [DataMember, MemoryPackOrder(12)] public Moment BeginsAt { get; init; }
    [DataMember, MemoryPackOrder(13)] public Moment? ClientSideBeginsAt { get; init; }
    [DataMember, MemoryPackOrder(14)] public Moment? EndsAt { get; init; }
    [DataMember, MemoryPackOrder(15)] public Moment? ContentEndsAt { get; init; }
    [DataMember, MemoryPackOrder(16)] public string Content { get; init; } = "";
    [DataMember, MemoryPackOrder(17)] public SystemEntry? SystemEntry { get; init; }
    [DataMember, MemoryPackOrder(18)] public bool HasReactions { get; init; }
    [DataMember, MemoryPackOrder(19)] public Symbol StreamId { get; init; } = "";
    [DataMember, MemoryPackOrder(20)] public long? AudioEntryId { get; init; }
    [DataMember, MemoryPackOrder(21)] public long? VideoEntryId { get; init; }
    [DataMember, MemoryPackOrder(22)] public LinearMap TimeMap { get; init; }
    [DataMember, MemoryPackOrder(23)] public long? RepliedEntryLocalId { get; init; }
    [DataMember, MemoryPackOrder(24)] public ChatEntryId ForwardedChatEntryId { get; init; }
    [DataMember, MemoryPackOrder(25)] public AuthorId ForwardedAuthorId { get; init; }
    [DataMember, MemoryPackOrder(50)] public ApiArray<TextEntryAttachment> Attachments { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public long LocalId => Id.LocalId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public ChatEntryKind Kind => Id.Kind;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public double? Duration => EndsAt is {} endsAt ? (endsAt - BeginsAt).TotalSeconds : null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool IsStreaming => !StreamId.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool IsSystemEntry => SystemEntry != null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool HasAudioEntry => AudioEntryId.HasValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool HasVideoEntry => VideoEntryId.HasValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool HasMediaEntry => VideoEntryId.HasValue || AudioEntryId.HasValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool HasMarkup => Kind == ChatEntryKind.Text && !IsSystemEntry && !HasMediaEntry;

    [MemoryPackConstructor]
    public ChatEntry() : this(ChatEntryId.None) { }

    // This record relies on version-based equality
    public bool Equals(ChatEntry? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}
