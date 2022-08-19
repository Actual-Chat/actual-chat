namespace ActualChat;

public static partial class StandardError
{
    public static Exception NotFound(string message)
        => new KeyNotFoundException(message);
    public static Exception NotFound<TTarget>(string? message = null)
        => NotFound(typeof(TTarget), message);
    public static Exception NotFound(Type target, string? message = null)
        => NotFound(target.Name, message);
    public static Exception NotFound(string target, string? message)
        => NotFound(message.IsNullOrEmpty()
            ? $"{target} is not found."
            : $"{target}: {message}");

    public static Exception StateTransition<TTarget>(string message)
        => StateTransition(typeof(TTarget), message);
    public static Exception StateTransition(Type target, string message)
        => StateTransition(target.Name, message);
    public static Exception StateTransition(string target, string message)
        => StateTransition($"Invalid {target} state transition: {message}");
    public static Exception StateTransition(string message)
        => new InvalidOperationException(message);

    public static Exception Constraint(string message)
        => new InvalidOperationException(message);
    public static Exception Constraint<TTarget>(string message)
        => Constraint(typeof(TTarget), message);
    public static Exception Constraint(Type target, string message)
        => Constraint(target.Name, message);
    public static Exception Constraint(string target, string message)
        => Constraint($"Invalid {target}: {message}");

    public static Exception Format(string message)
        => new FormatException(message);
    public static Exception Format<TTarget>(string? message = null)
        => Format(typeof(TTarget), message);
    public static Exception Format(Type target, string? message = null)
        => Format(target.Name, message);
    public static Exception Format(string target, string? message)
        => Format(message.IsNullOrEmpty() ? $"Invalid {target} format." : $"Invalid {target} format: {message}");

    public static Exception NotSupported(string message)
        => new NotSupportedException(message);
    public static Exception NotSupported<TTarget>(string message)
        => NotSupported(typeof(TTarget), message);
    public static Exception NotSupported(Type target, string message)
        => NotSupported(target.Name, message);
    public static Exception NotSupported(string target, string message)
        => NotSupported($"{target}: {message}");

    public static Exception Unavailable(string message)
        => new InvalidOperationException(message);

    public static Exception Unauthorized(string message)
        => new UnauthorizedAccessException(message);

    public static Exception Internal(string message)
        => new InternalError(message);
}
