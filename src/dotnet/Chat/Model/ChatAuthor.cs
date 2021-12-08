using ActualChat.Users;

namespace ActualChat.Chat;

public record ChatAuthor : Author
{
    public Symbol ChatId { get; init; }
    public Symbol UserId { get; init; }
}
