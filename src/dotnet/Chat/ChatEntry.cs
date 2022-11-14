using ActualChat.Comparison;
using Stl.Versioning;

namespace ActualChat.Chat;

public sealed record ChatEntry : IHasId<long>, IHasVersion<long>, IRequirementTarget
{
    private static IEqualityComparer<ChatEntry> EqualityComparer { get; } =
        VersionBasedEqualityComparer<ChatEntry, long>.Instance;

    public Symbol ChatId { get; init; }
    public ChatEntryType Type { get; init; }
    public long Id { get; init; }
    public long Version { get; init; }
    public bool IsRemoved { get; init; }
    public Symbol AuthorId { get; init; }
    public Moment BeginsAt { get; init; }
    public Moment? ClientSideBeginsAt { get; init; }
    public Moment? EndsAt { get; init; }
    public Moment? ContentEndsAt { get; init; }
    public string Content { get; init; } = "";
    public ServiceEntryDetails? ServiceEntry { get; init; }
    public bool IsServiceEntry => ServiceEntry != null;
    public bool HasReactions { get; init; }
    public Symbol StreamId { get; init; } = "";
    public long? AudioEntryId { get; init; }
    public long? VideoEntryId { get; init; }
    public LinearMap TextToTimeMap { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public double? Duration
        => EndsAt is {} endsAt ? (endsAt - BeginsAt).TotalSeconds : null;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsStreaming => !StreamId.IsEmpty;

    public long? RepliedChatEntryId { get; init; }
    public ImmutableArray<TextEntryAttachment> Attachments { get; init; } = ImmutableArray<TextEntryAttachment>.Empty;

    public Symbol CompositeId { get; init; } = "";

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
