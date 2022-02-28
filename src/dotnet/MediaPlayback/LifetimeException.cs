namespace ActualChat.MediaPlayback;

[Serializable]
public class LifetimeException : Exception
{
    public LifetimeException() { }
    public LifetimeException(string? message) : base(message) { }
    public LifetimeException(string? message, Exception? innerException) : base(message, innerException) { }
    protected LifetimeException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        : base(serializationInfo, streamingContext) { }
}
