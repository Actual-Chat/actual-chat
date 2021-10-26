#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat
{
    public record Chat(ChatId Id)
    {
        public string Title { get; init; } = "";
        public DateTime CreatedAt { get; init; }
        public bool IsPublic { get; init; }
        public ImmutableArray<UserId> OwnerIds { get; init; } = ImmutableArray<UserId>.Empty;

        public Chat() : this("") { }
    }
}
