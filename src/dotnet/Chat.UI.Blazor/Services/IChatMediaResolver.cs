namespace ActualChat.Chat.UI.Blazor.Services;

public interface IChatMediaResolver
{
    public Uri GetAudioBlobUri(ChatEntry audioEntry);
    public Uri GetVideoBlobUri(ChatEntry videoEntry);
}
