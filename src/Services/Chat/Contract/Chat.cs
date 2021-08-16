using System;
using System.Collections.Immutable;

namespace ActualChat.Chat
{
    public record Chat(string ChatId)
    {
        public ImmutableHashSet<string> OwnerIds { get; init; } = ImmutableHashSet<string>.Empty;
        public bool IsPublic { get; init; }

        public Chat() : this("") { }
    }
}
