namespace ActualChat.Video;

public class VideoStreamLimitExceededException : Exception
{
    public ChatId ChatId { get; }
    public int CurrentCount { get; }

    public VideoStreamLimitExceededException(ChatId chatId, int currentCount)
        : base($"Video stream limit reached for chat '{chatId}': {currentCount}/{Constants.Video.MaxWebcamStreamsPerChat} webcam streams active.")
    {
        ChatId = chatId;
        CurrentCount = currentCount;
    }
}
