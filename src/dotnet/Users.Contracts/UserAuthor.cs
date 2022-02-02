namespace ActualChat.Users;

public sealed record UserAuthor : Author
{
    public Symbol AvatarId { get; init; }
}
