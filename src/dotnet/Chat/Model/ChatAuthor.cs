using ActualChat.Users;

namespace ActualChat.Chat;

public record ChatAuthor : Author
{
    public ChatId ChatId { get; init; }
    public UserId UserId { get; init; }
}
