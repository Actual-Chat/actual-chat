namespace ActualChat;

public static partial class StandardError
{
    public static class Upload
    {
        public static Exception NotFound(string? message = null)
            => new UploadNotFoundException(message);
        public static Exception OffsetConflict(string? message = null)
            => new OffsetConflictException(message);
        public static Exception TransientFailure(string? message = null)
            => new UploadTransientException(message);
    }
}

[Serializable]
public abstract class UploadException : Exception
{
    protected UploadException() : base() { }
    protected UploadException(string? message) : base(message) { }
    protected UploadException(string? message, Exception? innerException) : base(message, innerException) { }
}

[Serializable]
public class UploadNotFoundException : UploadException
{
    public UploadNotFoundException() : base() { }
    public UploadNotFoundException(string? message) : base(message) { }
    public UploadNotFoundException(string? message, Exception? innerException) : base(message, innerException) { }
}

[Serializable]
public class OffsetConflictException : UploadException
{
    public OffsetConflictException() : base() { }
    public OffsetConflictException(string? message) : base(message) { }
    public OffsetConflictException(string? message, Exception? innerException) : base(message, innerException) { }
}

[Serializable]
public class UploadTransientException : UploadException
{
    public UploadTransientException() : base() { }
    public UploadTransientException(string? message) : base(message) { }
    public UploadTransientException(string? message, Exception? innerException) : base(message, innerException) { }
}
