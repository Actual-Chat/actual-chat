namespace ActualChat.Transcription;

public class TranscriptException : Exception
{
    public TranscriptException(string? code, string? message) : base(message)
    {
        Code = code;
    }

    public TranscriptException() : base()
    {
    }

    public TranscriptException(string? message) : base(message)
    {
    }

    public TranscriptException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    public string? Code { get; init; }

}
