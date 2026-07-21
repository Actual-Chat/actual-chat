using CoreMedia;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Per-scope fan-out of the publisher's captured camera frames. The in-call self-tile
/// preview taps these instead of opening a second <c>AVCaptureSession</c>, which starves
/// the publisher's session on macOS (capture stops, the outbound stream silently stalls).
/// </summary>
public sealed class AppleCameraFrameTap
{
    public event Action<CMSampleBuffer, CMTime>? FrameCaptured;
    public event Action<bool>? SourceChanged;

    public bool HasSource { get; private set; }

    public void SetSource(bool hasSource)
    {
        if (HasSource == hasSource)
            return;

        HasSource = hasSource;
        SourceChanged?.Invoke(hasSource);
    }

    public void Publish(CMSampleBuffer sampleBuffer, CMTime pts)
        => FrameCaptured?.Invoke(sampleBuffer, pts);
}
