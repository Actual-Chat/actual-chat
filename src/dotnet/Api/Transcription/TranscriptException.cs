namespace ActualChat.Transcription;

#pragma warning disable SYSLIB0051 // Type or member is obsolete

public class TranscriptionException : Exception
{
    public string? Code { get; init; }

    public TranscriptionException(string? code, string? message)
        : base(code.IsNullOrEmpty() ? message : $"[{code}] {message}")
        => Code = code;

    public TranscriptionException() : base() { }
    public TranscriptionException(string? message) : base(message) { }
    public TranscriptionException(string? message, Exception? innerException) : base(message, innerException) { }
    protected TranscriptionException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
