
using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing;

internal enum ChatEventType
{
    New,
    Update,
    Remove,
}

internal sealed record ChatEvent(ChatEventType Type, ChatEntry ChatEntry) : IHasId<ChatEntryCursor>
{
    public ChatEntryCursor Id => new(ChatEntry.Version, ChatEntry.LocalId);
}
