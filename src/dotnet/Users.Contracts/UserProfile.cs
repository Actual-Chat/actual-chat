namespace ActualChat.Users;

public sealed record UserProfile(Symbol Id, User User)
{
    public static UserProfile Guest { get; } = new(Symbol.Empty, User.NewGuest()) { Status = UserStatus.Inactive };

    public long Version { get; init; }
    public UserStatus Status { get; init; }
    public Symbol AvatarId { get; init; }
    public bool IsAdmin { get; init; }
}
