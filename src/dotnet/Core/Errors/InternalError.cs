namespace ActualChat;

#pragma warning disable SYSLIB0051 // Type or member is obsolete

[Serializable]
public class InternalError : Exception
{
    public InternalError() { }
    public InternalError(string? message) : base(message) { }
    public InternalError(string? message, Exception? innerException) : base(message, innerException) { }
    protected InternalError(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
