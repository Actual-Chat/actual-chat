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
    [DataMember] public SystemEntryContent? ServiceEntry { get; init; }
    [DataMember] public bool HasReactions { get; init; }
    [DataMember] public Symbol StreamId { get; init; } = "";
    [DataMember] public long? AudioEntryId { get; init; }
    [DataMember] public long? VideoEntryId { get; init; }
    [DataMember] public LinearMap TextToTimeMap { get; init; }
    [DataMember] public long? RepliedEntryLocalId { get; init; }
    [DataMember] public ImmutableArray<TextEntryAttachment> Attachments { get; init; } = ImmutableArray<TextEntryAttachment>.Empty;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long LocalId => Id.LocalId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatEntryKind Kind => Id.EntryKind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsServiceEntry => ServiceEntry != null;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public double? Duration => EndsAt is {} endsAt ? (endsAt - BeginsAt).TotalSeconds : null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsStreaming => !StreamId.IsEmpty;

    public ChatEntry() : this(ChatEntryId.None) { }

    public string GetContentOrDescription()
    {
        if (!Content.IsNullOrEmpty())
            return Content;

        var imageCount = Attachments.Count(x => x.IsImage());
        var description = imageCount switch {
            1 => "image",
            > 1 => "images",
            0 when Attachments.Length == 1 => Attachments[0].FileName,
            _ => "files: " + string.Join(", ", Attachments.Select(x => x.FileName)),
        };
        return description;
    }

    // This record relies on version-based equality
    public bool Equals(ChatEntry? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}
