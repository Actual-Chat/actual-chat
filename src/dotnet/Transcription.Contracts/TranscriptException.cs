namespace ActualChat.Transcription;

public class TranscriptException : Exception
{
    public TranscriptException(string? code, string? message) : base(message)
    {
        Code = code;
    }

    public string? Code { get; init; }

}
