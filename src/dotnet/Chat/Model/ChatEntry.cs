using System.Text.Json.Serialization;
using Stl.Versioning;

namespace ActualChat.Chat;

public record ChatEntry : IHasId<long>, IHasVersion<long>
{
    public Symbol ChatId { get; init; }
    public ChatEntryType Type { get; init; }
    public long Id { get; init; }
    public long Version { get; init; }
    public Symbol AuthorId { get; init; }
    public Moment BeginsAt { get; init; }
    public Moment? EndsAt { get; init; }
    public string Content { get; init; } = "";

    public Symbol StreamId { get; init; } = "";
    public long? AudioEntryId { get; init; }
    public long? VideoEntryId { get; init; }
    public LinearMap TextToTimeMap { get; init; }
    public string? Metadata { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public double? Duration
        => EndsAt == null ? null : (EndsAt.Value - BeginsAt).TotalSeconds;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsStreaming => !StreamId.IsEmpty;
}
