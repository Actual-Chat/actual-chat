using ActualChat.Video;

namespace ActualChat.App.Maui.Services.Recording;

public record VideoCaptureConfig(
    int Width = 1280,
    int Height = 720,
    int Fps = 30,
    int Bitrate = 2_000_000,
    bool UseFrontCamera = true);

public interface INativeVideoCapture
{
    int CaptureWidth { get; }
    int CaptureHeight { get; }
    Task<IAsyncEnumerable<VideoFrame>> StartCapture(VideoCaptureConfig config, CancellationToken ct);
    Task StopCapture();
    void Reconfigure(int width, int height, int bitrate);
    void SetBackgroundBlur(bool enabled);
    void ShowPreview();
    void HidePreview();
}
