namespace ActualChat;

#pragma warning disable SYSLIB0051 // Type or member is obsolete

[Serializable]
public abstract class NotFoundException : Exception, INotFoundException
{
    protected NotFoundException() { }
    protected NotFoundException(string? message) : base(message) { }
    protected NotFoundException(string? message, Exception? innerException) : base(message, innerException) { }
    protected NotFoundException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}

[Serializable]
public class NotFoundException<TTarget> : NotFoundException
{
    public Type TargetType => typeof(TTarget);

    public NotFoundException() { }
    public NotFoundException(string? message) : base(message) { }
    public NotFoundException(string? message, Exception? innerException) : base(message, innerException) { }
    protected NotFoundException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
