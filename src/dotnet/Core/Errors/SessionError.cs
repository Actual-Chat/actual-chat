namespace ActualChat;

[Serializable]
public class SessionError : Exception
{
    public SessionError() { }
    public SessionError(string? message) : base(message) { }
    public SessionError(string? message, Exception? innerException) : base(message, innerException) { }
    protected SessionError(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
