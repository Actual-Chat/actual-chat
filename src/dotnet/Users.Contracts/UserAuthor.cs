namespace ActualChat.Users;

public sealed record UserAuthor : Author
{
    // Must be used for other people's accounts only!
    public static Requirement<UserAuthor> MustExist { get; } = Requirement.New(
        new(() => StandardError.UserAuthor.Unavailable()),
        (UserAuthor? p) => p != null);
}
