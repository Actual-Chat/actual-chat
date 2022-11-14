using ActualChat.Comparison;
using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract]
public sealed record ChatEntry : IHasId<ChatEntryId>, IHasId<long>, IHasVersion<long>, IRequirementTarget
{
    private static IEqualityComparer<ChatEntry> EqualityComparer { get; } =
        VersionBasedEqualityComparer<ChatEntry, long>.Instance;

    long IHasId<long>.Id => LocalId;
    [DataMember] public ChatEntryId Id { get; init; }
    [DataMember] public long Version { get; init; }
    [DataMember] public bool IsRemoved { get; init; }
    [DataMember] public Symbol AuthorId { get; init; }
    [DataMember] public Moment BeginsAt { get; init; }
    [DataMember] public Moment? ClientSideBeginsAt { get; init; }
    [DataMember] public Moment? EndsAt { get; init; }
    [DataMember] public Moment? ContentEndsAt { get; init; }
    [DataMember] public string Content { get; init; } = "";
    [DataMember] public bool HasReactions { get; init; }
    [DataMember] public Symbol StreamId { get; init; } = "";
    [DataMember] public long? AudioEntryId { get; init; }
    [DataMember] public long? VideoEntryId { get; init; }
    [DataMember] public LinearMap TextToTimeMap { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long LocalId => Id.LocalId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatEntryKind Kind => Id.EntryKind;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public double? Duration
        => EndsAt is {} endsAt ? (endsAt - BeginsAt).TotalSeconds : null;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsStreaming => !StreamId.IsEmpty;

    public long? RepliedChatEntryId { get; init; }
    public ImmutableArray<TextEntryAttachment> Attachments { get; init; } = ImmutableArray<TextEntryAttachment>.Empty;

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
    public bool Equals(ChatEntry? other)
        => EqualityComparer.Equals(this, other);
    public override int GetHashCode()
        => EqualityComparer.GetHashCode(this);
}
