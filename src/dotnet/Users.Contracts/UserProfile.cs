namespace ActualChat.Users;

public sealed record UserProfile
{
    public static UserProfile Guest { get; } = new(Symbol.Empty, new User("Guest"));

    public Symbol Id { get; init; }
    public User User { get; init; }
    public bool IsAdmin { get; init; }
    public UserStatus Status { get; set; }
    public long Version { get; set; }
    public string Picture { get; set; } = "";

    public UserProfile(Symbol id, User user)
    {
        Id = id;
        User = user;
    }
}
