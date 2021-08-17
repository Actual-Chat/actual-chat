using System;
using System.Text.Json.Serialization;
using Stl.Text;
using Stl.Time;

namespace ActualChat.Chat
{
    public record ChatEntry(ChatEntryKind Kind, long Id)
    {
        public bool IsRemoved { get; init; }
        public Symbol UserId { get; init; }
        public Moment BeginsAt { get; init; }
        public Moment EndsAt { get; init; }
        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public double Duration => (EndsAt - BeginsAt).TotalSeconds;
        public string Content { get; init; } = "";

        public ChatEntry() : this(ChatEntryKind.Text, 0) { }
    }
}
