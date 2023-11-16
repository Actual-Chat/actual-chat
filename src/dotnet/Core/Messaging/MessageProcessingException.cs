namespace ActualChat.Messaging;

#pragma warning disable SYSLIB0051 // Type or member is obsolete

[Serializable]
public class MessageProcessingException : Exception
{
    public object? ProcessedMessage { get; init; } = null;

    public MessageProcessingException() { }
    public MessageProcessingException(string? message) : base(message) { }
    public MessageProcessingException(string? message, Exception? innerException) : base(message, innerException) { }
    protected MessageProcessingException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        : base(serializationInfo, streamingContext) { }
}
