using System;
using System.Text.Json.Serialization;
using Stl.Text;
using Stl.Time;

namespace ActualChat.Chat
{
    public record ChatEntry(string ChatId, long Id)
    {
        public Symbol CreatorId { get; init; }
        public Moment BeginsAt { get; init; }
        public Moment EndsAt { get; init; }
        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public double Duration => (EndsAt - BeginsAt).TotalSeconds;
        public ChatContentType ContentType { get; init; }
        public string Content { get; init; } = "";

        public ChatEntry() : this("", 0) { }
    }
}
