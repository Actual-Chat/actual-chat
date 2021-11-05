namespace ActualChat.Users;

/// <summary>
/// Used as contact info in contacts list, etc.
/// </summary>
public record UserInfo(Symbol Id, string Name = "(unknown)")
{
    public static UserInfo None { get; } = new(Symbol.Empty, "(none)");
}
