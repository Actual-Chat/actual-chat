using System.Collections.Immutable;

namespace ActualChat.Chat
{
    public record ChatPage
    {
        public ImmutableArray<ChatEntry> Entries { get; init; }
    }
}
