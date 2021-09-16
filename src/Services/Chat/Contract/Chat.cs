using System;
using System.Collections.Immutable;

namespace ActualChat.Chat
{
    public record Chat(ChatId Id)
    {
        public string Title { get; init; } = "";
        public DateTime CreatedAt { get; init; }
        public UserId CreatorId { get; init; } = "";
        public bool IsPublic { get; init; }
        public ImmutableArray<UserId> OwnerIds { get; init; } = ImmutableArray<UserId>.Empty;

        public Chat() : this("") { }
    }
}
