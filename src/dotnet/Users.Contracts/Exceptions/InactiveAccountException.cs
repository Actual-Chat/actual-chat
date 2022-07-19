namespace ActualChat.Users;

public class InactiveAccountException : AccountException
{
    public InactiveAccountException() : this(null) { }
    public InactiveAccountException(string? message) : base(message ?? "Your account is not activated yet.") { }
    public InactiveAccountException(string? message, Exception? inner) : base(message, inner) { }
}
