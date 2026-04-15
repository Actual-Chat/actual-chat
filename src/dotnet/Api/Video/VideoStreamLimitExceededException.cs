namespace ActualChat.Video;

#pragma warning disable SYSLIB0051 // Type or member is obsolete

public class VideoStreamLimitExceededException : Exception
{
    public VideoStreamLimitExceededException()
        : this((string?)null)
    { }

    public VideoStreamLimitExceededException(ChatId chatId)
        : this($"Video stream limit reached for chat #{chatId}.")
    { }

    public VideoStreamLimitExceededException(string? message)
        : base(message ?? "Video stream limit reached.")
    { }

    public VideoStreamLimitExceededException(string? message, Exception? innerException)
        : base(message, innerException)
    { }
}
