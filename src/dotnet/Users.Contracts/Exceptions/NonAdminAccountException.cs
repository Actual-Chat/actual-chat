namespace ActualChat.Users;

public class NonAdminAccountException : AccountException
{
    public NonAdminAccountException() : this(null) { }
    public NonAdminAccountException(string? message) : base(message ?? "Only administrators can perform this action.") { }
    public NonAdminAccountException(string? message, Exception? inner) : base(message, inner) { }
}
