namespace ActualChat.Playback;

public interface IPlaybackManager
{
    [ComputeMethod(KeepAliveTime = 60)]
    Task<PlaybackState> Get(ChatId chatId);
    Task Set(ChatId chatId, PlaybackState playbackState);
}
