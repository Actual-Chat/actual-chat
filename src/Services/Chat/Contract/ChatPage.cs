using System.Collections.Immutable;
using ActualChat.Users;
using Stl.Text;

namespace ActualChat.Chat
{
    public record ChatPage(string ChatId, int Limit)
    {
        public ImmutableList<ChatEntry> Entries { get; init; } = ImmutableList<ChatEntry>.Empty;
        public ImmutableDictionary<Symbol, UserInfo> Users { get; init; } = ImmutableDictionary<Symbol, UserInfo>.Empty;

        public ChatPage() : this("", 0) { }
    }
}
