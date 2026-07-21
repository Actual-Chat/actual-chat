using ActualChat.Media;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Video;

namespace ActualChat.App.Maui.Video;

public sealed class MauiVideoPublisherFactory : INativeVideoPublisherFactory
{
    public INativeVideoPublisher Create(AppUIHub hub, VideoSourceKind kind, INativeVideoPublisherCallbacks callbacks)
        => new MauiVideoPublisher(hub, kind, callbacks);
}
