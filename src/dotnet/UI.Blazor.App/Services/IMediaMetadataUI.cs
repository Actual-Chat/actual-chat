namespace ActualChat.UI.Blazor.App.Services;

public record MediaMetadata(
    string Title,
    string Artist,
    string ImageUrl)
{
    public static MediaMetadata FromTrack(ChatAudioTrackInfo trackInfo)
    {
        var authorName = trackInfo.Author?.Avatar.Name ?? "Unknown";
        var chatTitle = trackInfo.Chat?.Title ?? "Unknown";
        return new($"{authorName} @ {chatTitle}", "Voxt", "/_applogo-dark_voxt.svg");
    }
}

public interface IMediaMetadataUI
{
    public Task SetPlayback(MediaMetadata metadata, bool isStreaming);
    public Task SetRecording(MediaMetadata metadata);
    public Task Reset();
}
