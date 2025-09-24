namespace ActualChat.UI.Blazor.App.Services;

public class WebMediaMetadataUI: IMediaMetadataUI
{
    public Task SetPlayback(MediaMetadata metadata, bool isStreaming)
        => Task.CompletedTask;

    public Task SetRecording(MediaMetadata metadata)
        => Task.CompletedTask;

    public Task Reset()
        => Task.CompletedTask;
}
