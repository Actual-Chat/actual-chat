namespace ActualChat.Users;

public class SuspendedAccountException : AccountException
{
    public SuspendedAccountException() : this(null) { }
    public SuspendedAccountException(string? message) : base(message ?? "Your account is suspended.") { }
    public SuspendedAccountException(string? message, Exception? inner) : base(message, inner) { }
}
