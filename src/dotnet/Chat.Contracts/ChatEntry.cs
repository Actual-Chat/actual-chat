using System.Text.Json.Serialization;
using ActualChat.Mathematics;

namespace ActualChat.Chat;

public record class ChatEntry(ChatId ChatId, long Id)
{
    public long Version { get; init; }
    public AuthorId AuthorId { get; init; }
    public Moment BeginsAt { get; init; }
    public Moment? EndsAt { get; init; }
    public ChatEntryType Type { get; init; }
    public string Content { get; init; } = "";

    public StreamId StreamId { get; init; } = "";
    public long? AudioEntryId { get; init; }
    public long? VideoEntryId { get; init; }
    public LinearMap? TextToTimeMap { get; init; }

    [JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public double? Duration
        => EndsAt == null ? null : (EndsAt.Value - BeginsAt).TotalSeconds;

    [JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public bool IsStreaming => !StreamId.IsNone;

    public ChatEntry() : this("", 0) { }
}
