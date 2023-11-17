namespace ActualChat;

#pragma warning disable CA1710 // Identifiers should have correct suffix
#pragma warning disable SYSLIB0051 // Type or member is obsolete

[Serializable]
public class ExternalError : Exception, ITransientException
{
    public ExternalError() { }
    public ExternalError(string? message) : base(message) { }
    public ExternalError(string? message, Exception? innerException) : base(message, innerException) { }
    protected ExternalError(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
