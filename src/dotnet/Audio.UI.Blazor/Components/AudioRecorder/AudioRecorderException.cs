namespace ActualChat.Audio.UI.Blazor.Components;

#pragma warning disable SYSLIB0051 // Type or member is obsolete

public class AudioRecorderException : Exception
{
    public AudioRecorderException() { }
    public AudioRecorderException(string? message) : base(message) { }
    public AudioRecorderException(string? message, Exception? innerException) : base(message, innerException) { }
    protected AudioRecorderException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
