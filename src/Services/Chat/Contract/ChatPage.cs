using System.Collections.Immutable;
using Stl.Time;

namespace ActualChat.Chat
{
    public record ChatPage
    {
        public ImmutableArray<ChatEntry> Entries { get; init; }
        public Range<Moment> TimeRange { get; init; }
    }
}
