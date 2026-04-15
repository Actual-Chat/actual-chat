namespace ActualChat;

/// <summary>
/// Base exception for indicating that a requested entity was not found.
/// </summary>
#pragma warning disable SYSLIB0051 // Type or member is obsolete
[Serializable]
public abstract class NotFoundException : Exception, INotFoundException
{
    protected NotFoundException() { }
    protected NotFoundException(string? message) : base(message) { }
    protected NotFoundException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception indicating that an entity of type <typeparamref name="TTarget"/> was not found.
/// </summary>
[Serializable]
public class NotFoundException<TTarget> : NotFoundException
{
    public Type TargetType => typeof(TTarget);

    public NotFoundException() { }
    public NotFoundException(string? message) : base(message) { }
    public NotFoundException(string? message, Exception? innerException) : base(message, innerException) { }
}
