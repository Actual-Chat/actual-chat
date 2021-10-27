namespace ActualChat.Chat;

public interface IChatMediaResolver
{
    public Uri GetAudioBlobUri(ChatEntry audioEntry);
    public Uri GetVideoBlobUri(ChatEntry videoEntry);
}
