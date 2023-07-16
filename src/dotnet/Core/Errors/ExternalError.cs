namespace ActualChat;

[Serializable]
public class ExternalError : Exception, ITransientException
{
    public ExternalError() { }
    public ExternalError(string? message) : base(message) { }
    public ExternalError(string? message, Exception? innerException) : base(message, innerException) { }
    protected ExternalError(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
