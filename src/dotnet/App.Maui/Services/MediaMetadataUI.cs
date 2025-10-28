using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;
public partial class MediaMetadataUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IMediaMetadataUI
{
    public partial Task SetPlayback(MediaMetadata metadata, bool isStreaming);

    public partial Task SetRecording(MediaMetadata metadata);

    public partial Task Reset();
}
