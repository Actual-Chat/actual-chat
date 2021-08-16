using System.Collections.Immutable;
using ActualChat.Users;
using Stl.Text;

namespace ActualChat.Chat
{
    public record ChatPage(string ChatId, int Limit)
    {
        public ImmutableList<ChatEntry> Entries { get; init; } = ImmutableList<ChatEntry>.Empty;
        public ImmutableDictionary<Symbol, Speaker> Speakers { get; init; } = ImmutableDictionary<Symbol, Speaker>.Empty;

        public ChatPage() : this("", 0) { }
    }
}
