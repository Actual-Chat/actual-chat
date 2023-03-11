using ActualChat.Comparison;
using Stl.Fusion.Blazor;
using Stl.Versioning;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract]
public sealed record ChatEntry(
    [property: DataMember] ChatEntryId Id,
    [property: DataMember] long Version = 0
    ) : IHasId<ChatEntryId>, IHasVersion<long>, IRequirementTarget
{
    public static IdAndVersionEqualityComparer<ChatEntry, ChatEntryId> EqualityComparer { get; } = new();

    public static Requirement<ChatEntry> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<ChatEntry>()),
        (ChatEntry? c) => c is { Id.IsNone: false });
    public static Requirement<ChatEntry> MustNotBeRemoved { get; } = Requirement.New(
        new(() => StandardError.NotFound<ChatEntry>()),
        (ChatEntry? c) => c is { Id.IsNone: false, IsRemoved: false });

    public static ChatEntry Removed(ChatEntryId id)
        => new (id) { IsRemoved = true };

    [DataMember] public bool IsRemoved { get; init; }
    [DataMember] public AuthorId AuthorId { get; init; }
    [DataMember] public Moment BeginsAt { get; init; }
    [DataMember] public Moment? ClientSideBeginsAt { get; init; }
    [DataMember] public Moment? EndsAt { get; init; }
    [DataMember] public Moment? ContentEndsAt { get; init; }
    [DataMember] public string Content { get; init; } = "";
    [DataMember] public SystemEntry? SystemEntry { get; init; }
    [DataMember] public bool HasReactions { get; init; }
    [DataMember] public Symbol StreamId { get; init; } = "";
    [DataMember] public long? AudioEntryId { get; init; }
    [DataMember] public long? VideoEntryId { get; init; }
    [DataMember] public LinearMap TimeMap { get; init; }
    [DataMember] public long? RepliedEntryLocalId { get; init; }
    [DataMember] public ImmutableArray<TextEntryAttachment> Attachments { get; init; } = ImmutableArray<TextEntryAttachment>.Empty;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long LocalId => Id.LocalId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatEntryKind Kind => Id.Kind;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public double? Duration => EndsAt is {} endsAt ? (endsAt - BeginsAt).TotalSeconds : null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsStreaming => !StreamId.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsSystemEntry => SystemEntry != null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool HasAudioEntry => AudioEntryId.HasValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool HasVideoEntry => VideoEntryId.HasValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool HasMediaEntry => VideoEntryId.HasValue || AudioEntryId.HasValue;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool HasMarkup => Kind == ChatEntryKind.Text && !IsSystemEntry && !HasMediaEntry;

    public ChatEntry() : this(ChatEntryId.None) { }

    // This record relies on version-based equality
    public bool Equals(ChatEntry? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}
