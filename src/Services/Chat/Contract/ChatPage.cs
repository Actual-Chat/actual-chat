using System.Collections.Immutable;
using ActualChat.Users;

namespace ActualChat.Chat
{
    public record ChatPage(string ChatId, int Limit)
    {
        public ImmutableList<ChatMessage> Messages { get; init; } = ImmutableList<ChatMessage>.Empty;
        public ImmutableDictionary<long, Speaker> Users { get; init; } = ImmutableDictionary<long, Speaker>.Empty;

        public ChatPage() : this("", 0) { }
    }
}
