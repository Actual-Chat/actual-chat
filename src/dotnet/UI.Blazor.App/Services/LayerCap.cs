namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Mutable pair of spatial-layer counts for camera + screencast.
/// Reduction goes camera-first; increase goes screencast-first.
/// </summary>
public sealed class LayerCap
{
    public int DeviceCameraCap { get; }
    public int ScreencastCap { get; }
    public int CameraLayers { get; private set; }
    public int ScreencastLayers { get; private set; }

    public LayerCap(int deviceCameraCap, int screencastCap, int? initialCameraLayers = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(deviceCameraCap, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(screencastCap, 1);
        DeviceCameraCap = deviceCameraCap;
        ScreencastCap = screencastCap;
        // Soft-start below the ceiling lets the QC ramp earn the top tier rather
        // than shipping it from the first frame.
        CameraLayers = initialCameraLayers is { } n ? Math.Clamp(n, 1, deviceCameraCap) : deviceCameraCap;
        ScreencastLayers = screencastCap;
    }

    public bool Reduce(IReadOnlyCollection<VideoSourceKind>? activeKinds = null)
    {
        var hasCamera = activeKinds is null || activeKinds.Contains(VideoSourceKind.Camera);
        var hasScreencast = activeKinds is null || activeKinds.Contains(VideoSourceKind.ScreenCast);
        if (hasCamera && CameraLayers > 1) {
            CameraLayers--;
            return true;
        }
        if (hasScreencast && ScreencastLayers > 1) {
            ScreencastLayers--;
            return true;
        }
        return false;
    }

    public bool Increase(IReadOnlyCollection<VideoSourceKind>? activeKinds = null)
    {
        var hasCamera = activeKinds is null || activeKinds.Contains(VideoSourceKind.Camera);
        var hasScreencast = activeKinds is null || activeKinds.Contains(VideoSourceKind.ScreenCast);
        if (hasScreencast && ScreencastLayers < ScreencastCap) {
            ScreencastLayers++;
            return true;
        }
        if (hasCamera && CameraLayers < DeviceCameraCap) {
            CameraLayers++;
            return true;
        }
        return false;
    }
}
