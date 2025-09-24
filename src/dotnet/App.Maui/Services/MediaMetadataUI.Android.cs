
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

public partial class MediaMetadataUI
{
    public partial Task SetPlayback(MediaMetadata metadata, bool isStreaming)
        => Task.CompletedTask;

    public partial Task SetRecording(MediaMetadata metadata)
        => Task.CompletedTask;

    public partial Task Reset()
        => Task.CompletedTask;
}
