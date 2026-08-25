using ActualChat.UI.Blazor.Resources;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Services;

public record MediaMetadata(
    string Title,
    string Artist,
    string ImageUrl)
{
    public static MediaMetadata FromTrack(ChatAudioTrackInfo trackInfo, IStringLocalizer l)
    {
        var authorName = trackInfo.Author?.Avatar.Name ?? l.ChatView_UnknownAuthor;
        var chatTitle = trackInfo.Chat?.Title ?? l.ChatView_UnknownChat;
        return new($"{authorName} @ {chatTitle}", CoreConstants.AppName, "/_applogo-dark_voxt.svg");
    }
}

public interface IMediaMetadataUI
{
    public Task SetPlayback(MediaMetadata metadata, bool isStreaming);
    public Task SetRecording(MediaMetadata metadata);
    public Task Reset();
}
