namespace ActualChat;

#pragma warning disable SYSLIB0051

[Serializable]
public class PostponeException : Exception // Must not be ITransientException!
{
    private const string DefaultMessage = "Postponed.";

    public TimeSpan Delay { get; init; }

    public PostponeException() : base(DefaultMessage) { }
    public PostponeException(string? message) : base(message ?? DefaultMessage) { }
    public PostponeException(string? message, Exception? innerException) : base(message ?? DefaultMessage, innerException) { }
    protected PostponeException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
