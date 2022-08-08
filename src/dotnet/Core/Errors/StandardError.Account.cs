namespace ActualChat;

public static partial class StandardError
{
    public static class Account
    {
        public static Exception None(string? message = null)
            => new NoAccountException(message);
        public static Exception Inactive(string? message = null)
            => new InactiveAccountException(message);
        public static Exception Suspended(string? message = null)
            => new SuspendedAccountException(message);
        public static Exception NonAdmin(string? message = null)
            => new NonAdminAccountException(message);

        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static Exception Unavailable(string? message = null)
            => new UnavailableAccountException(message);
    }
}

public abstract class AccountException : Exception
{
    protected AccountException() : this(null) { }
    protected AccountException(string? message) : base(message ?? "Account-related error.") { }
    protected AccountException(string? message, Exception? inner) : base(message, inner) { }
}

public class NoAccountException : AccountException
{
    public NoAccountException() : this(null) { }
    public NoAccountException(string? message) : base(message ?? "You must sign-in to perform this action.") { }
    public NoAccountException(string? message, Exception? inner) : base(message, inner) { }
}

public class InactiveAccountException : AccountException
{
    public InactiveAccountException() : this(null) { }
    public InactiveAccountException(string? message) : base(message ?? "Your account is not activated yet.") { }
    public InactiveAccountException(string? message, Exception? inner) : base(message, inner) { }
}

public class SuspendedAccountException : AccountException
{
    public SuspendedAccountException() : this(null) { }
    public SuspendedAccountException(string? message) : base(message ?? "Your account is suspended.") { }
    public SuspendedAccountException(string? message, Exception? inner) : base(message, inner) { }
}

public class NonAdminAccountException : AccountException
{
    public NonAdminAccountException() : this(null) { }
    public NonAdminAccountException(string? message) : base(message ?? "Only administrators can perform this action.") { }
    public NonAdminAccountException(string? message, Exception? inner) : base(message, inner) { }
}

public class UnavailableAccountException : AccountException
{
    public UnavailableAccountException() : this(null) { }
    public UnavailableAccountException(string? message) : base(message ?? "This user doesn't exit.") { }
    public UnavailableAccountException(string? message, Exception? inner) : base(message, inner) { }
}
