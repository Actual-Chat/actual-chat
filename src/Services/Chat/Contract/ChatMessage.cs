using System;
using Stl.Time;

namespace ActualChat.Chat
{
    public record ChatMessage(string Id, string ChatId)
    {
        public long UserId { get; init; }
        public Moment CreatedAt { get; init; }
        public Moment EditedAt { get; init; }
        public bool IsRemoved { get; init; }
        public string Text { get; init; } = "";

        public ChatMessage() : this("", "") { }
    }
}
