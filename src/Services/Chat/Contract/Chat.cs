using System;
using System.Collections.Immutable;

namespace ActualChat.Chat
{
    public record Chat(string Id)
    {
        public string Title { get; init; } = "";
        public DateTime CreatedAt { get; init; }
        public string CreatorId { get; init; } = "";
        public bool IsPublic { get; init; }
        public ImmutableArray<string> OwnerIds { get; init; } = ImmutableArray<string>.Empty;

        public Chat() : this("") { }
    }
}
