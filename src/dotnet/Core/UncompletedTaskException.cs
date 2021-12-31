namespace ActualChat;

[Serializable]
public class UncompletedTaskException : Exception
{
    public UncompletedTaskException()
        : this(null) { }
    // ReSharper disable once IntroduceOptionalParameters.Global
    public UncompletedTaskException(string? message)
        : this(message, null) { }
    public UncompletedTaskException(string? message, Exception? innerException)
        : base(message ?? "Task is not completed yet.", innerException) { }

    protected UncompletedTaskException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
}
