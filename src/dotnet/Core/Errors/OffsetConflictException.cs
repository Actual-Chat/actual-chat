namespace ActualChat;

[Serializable]
public class OffsetConflictException : Exception
{
    public OffsetConflictException() : base() { }
    public OffsetConflictException(string? message) : base(message) { }
    public OffsetConflictException(string? message, Exception? innerException) : base(message, innerException) { }
}
