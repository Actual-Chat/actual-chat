using System.Text.Json.Serialization;
using Stl;
using Stl.Text;
using Stl.Time;

namespace ActualChat.Chat
{
    public record ChatEntry(ChatId ChatId, long Id)
    {
        public UserId CreatorId { get; init; }
        public Moment BeginsAt { get; init; }
        public Moment EndsAt { get; init; }
        public ChatContentType ContentType { get; init; }
        public string Content { get; init; } = "";
        public StreamId StreamId { get; init; } = "";

        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public double Duration => (EndsAt - BeginsAt).TotalSeconds;
        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public bool IsStreaming => !StreamId.IsNullOrEmpty();

        public ChatEntry() : this("", 0) { }
    }
}
