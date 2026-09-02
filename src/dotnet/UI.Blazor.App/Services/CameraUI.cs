using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Camera device selection, enumeration, switching, and mirror state.
/// In-memory only; <see cref="LocalAppSettings.SelectedCameraDeviceId"/> persists the choice.
/// </summary>
public class CameraUI : UIServiceBase<AppUIHub>, IComputeService
{
    private static readonly string JSEnumerateDevices =
        $"{BlazorUIAppModule.ImportName}.CameraDevices.enumerateDevices";
    private static readonly string JSHasCamera =
        $"{BlazorUIAppModule.ImportName}.CameraDevices.hasCamera";

    private readonly MutableState<string?> _selectedDeviceId;
    private readonly MutableState<bool> _isMirrored;

    public string? LastDeviceId { get; private set; }
    public string? LastFacingMode { get; private set; }

    public CameraUI(AppUIHub hub) : base(hub)
    {
        _selectedDeviceId = StateFactory.NewMutable((string?)null);
        _isMirrored = StateFactory.NewMutable(true);
    }

    [ComputeMethod]
    public virtual async Task<string?> GetSelectedDeviceId(CancellationToken cancellationToken = default)
        => await _selectedDeviceId.Use(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> GetIsMirrored(CancellationToken cancellationToken = default)
        => await _isMirrored.Use(cancellationToken).ConfigureAwait(false);

    public void SetSelectedDevice(string? deviceId)
        => _selectedDeviceId.Value = deviceId;

    public async Task<bool> HasCamera()
    {
        // Unlike EnumerateDevices, never raises a permission prompt - see CameraDevices.hasCamera.
        try {
            return await JS.InvokeAsync<bool>(JSHasCamera).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "HasCamera failed");
            return false;
        }
    }

    public async Task<VideoDevice[]> EnumerateDevices(bool includeAll = false)
    {
        try {
            return await JS.InvokeAsync<VideoDevice[]>(JSEnumerateDevices, includeAll).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "EnumerateDevices failed");
            return [];
        }
    }

    public async Task SwitchCamera()
    {
        var currentId = _selectedDeviceId.Value;
        var nextDevice = await GetCameraToSwitch(currentId).ConfigureAwait(false);
        if (nextDevice is null)
            return;

        await SelectCamera(nextDevice.DeviceId).ConfigureAwait(false);
    }

    public async Task<VideoDevice?> GetCameraToSwitch(string? currentDeviceId)
    {
        var devices = await EnumerateDevices().ConfigureAwait(false);
        if (devices.Length == 0)
            return null;

        if (devices.Length == 1)
            return devices[0];

        var currentIndex = Array.FindIndex(devices, d => d.DeviceId == currentDeviceId);
        var nextIndex = currentIndex < 0
            ? devices.Length - 1
            : (currentIndex + 1) % devices.Length;
        return devices[nextIndex];
    }

    public async Task SelectCamera(string deviceId)
    {
        await LocalSettings.LocalAppSettings()
            .Update(s => s with { SelectedCameraDeviceId = deviceId })
            .ConfigureAwait(false);
        SetSelectedDevice(deviceId);
    }

    internal void OnTrackSettings(string? deviceId, string? facingMode)
    {
        // Called by the active camera recorder after each track acquisition
        // (start or camera switch). Resolves per-camera mirror override so
        // the live self-preview reflects the right camera regardless of
        // how the stream was started.
        LastDeviceId = deviceId;
        LastFacingMode = facingMode;
        _ = ApplyAsync();
        return;

        async Task ApplyAsync() {
            var settings = await LocalSettings.LocalAppSettings().Get().ConfigureAwait(false);
            _isMirrored.Value = settings
                .ResolveIsCameraMirrored(deviceId, facingMode, Hub.BrowserInfo.IsMobile);
        }
    }

    public void ReapplyMirror()
        // Re-resolves the live preview against a just-persisted mirror override, rather than
        // leaving it stale until the next camera acquisition.
        => OnTrackSettings(LastDeviceId, LastFacingMode);
}
