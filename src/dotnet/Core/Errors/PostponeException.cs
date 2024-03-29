namespace ActualChat;

#pragma warning disable SYSLIB0051

[Serializable]
public class PostponeException : Exception // Must not be ITransientException!
{
    public TimeSpan Delay { get; init; }

    public PostponeException() { }
    public PostponeException(string? message) : base(message) { }
    public PostponeException(string? message, Exception? innerException) : base(message, innerException) { }
    protected PostponeException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
