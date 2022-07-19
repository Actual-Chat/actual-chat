namespace ActualChat.Users;

public class NoAccountException : AccountException
{
    public NoAccountException() : this(null) { }
    public NoAccountException(string? message) : base(message ?? "You must sign-in to perform this action.") { }
    public NoAccountException(string? message, Exception? inner) : base(message, inner) { }
}
