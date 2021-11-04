namespace ActualChat.Users;

/// <summary>
/// It will be used as contact info in a contact list.
/// </summary>
public record UserInfo(Symbol Id, string Name = "(unknown)")
{
    public static UserInfo None { get; } = new();

    public UserInfo() : this(Symbol.Empty, "(none)") { }
}
