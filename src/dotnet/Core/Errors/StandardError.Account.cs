namespace ActualChat;

public static partial class StandardError
{
    public static class Account
    {
        public static Exception Guest(string? message = null)
            => new GuestAccountException(message);
        public static Exception Inactive(string? message = null)
            => new InactiveAccountException(message);
        public static Exception Suspended(string? message = null)
            => new SuspendedAccountException(message);
        public static Exception NonAdmin(string? message = null)
            => new NonAdminAccountException(message);
    }
}

/// <summary>
/// Base exception for account-related errors.
/// </summary>
public abstract class AccountException : Exception
{
    protected AccountException() : this(null) { }
    protected AccountException(string? message) : base(message ?? "Account-related error.") { }
    protected AccountException(string? message, Exception? inner) : base(message, inner) { }
}

/// <summary>
/// Exception thrown when a guest user attempts an action requiring authentication.
/// </summary>
public class GuestAccountException : AccountException
{
    public GuestAccountException() : this(null) { }
    public GuestAccountException(string? message) : base(message ?? "You must sign-in to perform this action.") { }
    public GuestAccountException(string? message, Exception? inner) : base(message, inner) { }
}

/// <summary>
/// Exception thrown when an inactive account attempts an action.
/// </summary>
public class InactiveAccountException : AccountException
{
    public InactiveAccountException() : this(null) { }
    public InactiveAccountException(string? message) : base(message ?? "Your account is not activated yet.") { }
    public InactiveAccountException(string? message, Exception? inner) : base(message, inner) { }
}

/// <summary>
/// Exception thrown when a suspended account attempts an action.
/// </summary>
public class SuspendedAccountException : AccountException
{
    public SuspendedAccountException() : this(null) { }
    public SuspendedAccountException(string? message) : base(message ?? "Your account is suspended.") { }
    public SuspendedAccountException(string? message, Exception? inner) : base(message, inner) { }
}

/// <summary>
/// Exception thrown when a non-admin user attempts an admin-only action.
/// </summary>
public class NonAdminAccountException : AccountException
{
    public NonAdminAccountException() : this(null) { }
    public NonAdminAccountException(string? message) : base(message ?? "Only administrators can perform this action.") { }
    public NonAdminAccountException(string? message, Exception? inner) : base(message, inner) { }
}
