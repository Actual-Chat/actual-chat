namespace ActualChat.Users;

public sealed record UserProfile(Symbol Id, User User)
{
    public static UserProfile Guest { get; } = new(Symbol.Empty, new User("Guest"));

    public bool IsAdmin { get; init; }
    public UserStatus Status { get; set; }
    public long Version { get; set; }
    public string Picture { get; set; } = "";
}
