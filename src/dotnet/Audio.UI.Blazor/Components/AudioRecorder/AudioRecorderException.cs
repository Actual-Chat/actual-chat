namespace ActualChat.Audio.UI.Blazor.Components;

public class AudioRecorderException : Exception
{
    public AudioRecorderException() { }
    protected AudioRecorderException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    public AudioRecorderException(string? message) : base(message) { }
    public AudioRecorderException(string? message, Exception? innerException) : base(message, innerException) { }
}
