namespace ActualChat.Users;

public abstract class AccountException : Exception
{
    protected AccountException() : this(null) { }
    protected AccountException(string? message) : base(message ?? "Account-related error.") { }
    protected AccountException(string? message, Exception? inner) : base(message, inner) { }
}
