namespace ActualChat.Users;

public sealed record UserProfile(Symbol Id, User User)
{
    public static UserProfile Guest { get; } = new(Symbol.Empty, new User("Guest")) { Status = UserStatus.Inactive };

    public long Version { get; set; }
    public string Picture { get; set; } = "";
    public UserStatus Status { get; set; }
    public bool IsAdmin { get; init; }
}
