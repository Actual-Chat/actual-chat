namespace ActualChat.Users;

public sealed record UserProfile : Author
{
    public static UserProfile Guest { get; } = new(Symbol.Empty, "Guest", new User("Guest"));

    public User User { get; init; }
    public bool IsAdmin { get; init; }

    public UserProfile(Symbol id, string name, User user)
    {
        Id = id;
        Name = name;
        User = user;
    }
}
