using ActualChat.Media;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Services.Video;

/// <summary>
/// Native (non-JS) outbound video pipeline: camera/screen capture, encode, and
/// publish to <c>ILiveVideoStreams.PushStream</c>. Implemented on Apple platforms
/// where WebCodecs is unavailable; <see cref="VideoRecorder"/> delegates to it in
/// place of the JS recorder. Method surface mirrors the JS recorder 1:1.
/// </summary>
public interface INativeVideoPublisher : IAsyncDisposable
{
    Task StartRecording(ChatId chatId, IReadOnlyList<string> supportedCodecs, int maxLayerCount, CancellationToken cancellationToken);
    Task Warmup(ChatId chatId, IReadOnlyList<string> supportedCodecs, CancellationToken cancellationToken);
    Task OpenGate(int maxLayerCount, CancellationToken cancellationToken);
    Task CancelWarmup(CancellationToken cancellationToken);
    Task StartScreenCast(ChatId chatId, IReadOnlyList<string> supportedCodecs, int maxLayerCount, CancellationToken cancellationToken);
    Task StopRecording(CancellationToken cancellationToken);
    Task SetSelectedCamera(string deviceId, CancellationToken cancellationToken);
    Task SwitchCamera(string deviceId, CancellationToken cancellationToken);
    Task SetBlurEnabled(bool enabled, CancellationToken cancellationToken);
    Task ToggleBlur(bool enabled, CancellationToken cancellationToken);
    Task SetLayers(IReadOnlyList<VideoLayerDef>? layers, CancellationToken cancellationToken);
    Task SetFpsCeiling(int maxFps, CancellationToken cancellationToken);
    Task ForceKeyFrame(CancellationToken cancellationToken);
    Task SetDemandedLayers(int mask, CancellationToken cancellationToken);
    Task SetThumbnailOnly(bool thumbnailOnly, CancellationToken cancellationToken);
    Task SetSpeaking(bool speaking, CancellationToken cancellationToken);
    Task SetRemoteStreamCount(int count, CancellationToken cancellationToken);
    Task UpdateSupportedDecoderCodecs(IReadOnlyList<string> codecs, CancellationToken cancellationToken);
}

/// <summary>
/// Callbacks a native publisher raises back into <see cref="VideoRecorder"/>, which
/// fans them out to <c>ChatVideoUI</c>/<c>CameraUI</c>/<c>VideoQualityUI</c> exactly
/// as the JS recorder's <c>[JSInvokable]</c> callbacks do.
/// </summary>
public interface INativeVideoPublisherCallbacks
{
    void OnRecordingStarted();
    void OnRecordingStopped();
    void OnRecordingError(string error);
    void OnTrackSettings(string? deviceId, string? facingMode);
    void OnRecorderStats(RecorderStats stats);
}

public interface INativeVideoPublisherFactory
{
    INativeVideoPublisher Create(AppUIHub hub, VideoSourceKind kind, INativeVideoPublisherCallbacks callbacks);
}

/// <summary>
/// Native camera enumeration for platforms where the WebView's
/// <c>enumerateDevices</c> returns no video inputs (Mac Catalyst WKWebView).
/// <see cref="CameraUI"/> prefers this over the JS path when registered.
/// </summary>
public interface INativeCameraDevices
{
    VideoDevice[] List();
}
