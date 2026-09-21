using Android;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.Content;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Opens the car's hands-free (SCO) channel as an HFP voice-recognition session - the way a
/// voice assistant does - instead of the virtual call Android raises for a communication route,
/// so the head unit never sees a call. See docs/live-audio/11-android-auto.md.
/// </summary>
internal sealed class AndroidVoiceRecognitionLink : IDisposable
{
    private static readonly TimeSpan ProxyTimeout = TimeSpan.FromSeconds(3);
    private const int AudioSettleChecks = 50;
    private const int AudioSettleCheckPeriod = 100;

    private readonly Context _context;
    private readonly ILogger _log;
    private readonly BluetoothAdapter? _adapter;
    private HeadsetProfileListener? _listener;
    private TaskCompletionSource<BluetoothHeadset?>? _headsetSource;
    private BluetoothDevice? _device;

    public bool IsActive => _device != null;

    public AndroidVoiceRecognitionLink(Context context, ILogger log)
    {
        _context = context;
        _log = log;
        _adapter = (context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;
    }

    public void Dispose()
    {
        Stop();
        if (_headsetSource?.Task is { IsCompletedSuccessfully: true, Result: { } headset })
            try {
                _adapter?.CloseProfileProxy(ProfileType.Headset, headset);
            }
            catch { /* Ignore */ }
        _headsetSource = null;
        _listener?.Dispose();
        _listener = null;
    }

    public static bool HasPermission(Context context)
        // BLUETOOTH_CONNECT is what the headset proxy calls check; below API 31 the install-time
        // BLUETOOTH permission covers them.
        => !OperatingSystem.IsAndroidVersionAtLeast(31)
            || ContextCompat.CheckSelfPermission(context, Manifest.Permission.BluetoothConnect) == Permission.Granted;

    public async Task<bool> Start(CancellationToken cancellationToken)
    {
        if (_device != null)
            return true;
        if (!HasPermission(_context)) {
            _log.LogWarning("Voice-recognition link: no BLUETOOTH_CONNECT permission");
            return false;
        }

        var headset = await GetHeadset(cancellationToken).ConfigureAwait(false);
        if (headset == null) {
            _log.LogWarning("Voice-recognition link: the headset profile proxy isn't available");
            return false;
        }

        try {
            var device = PickCar(headset.ConnectedDevices ?? []);
            if (device == null) {
                _log.LogWarning("Voice-recognition link: no connected hands-free device");
                return false;
            }

            _log.LogInformation("Voice-recognition link: starting on {Device}", device.Name);
            if (!headset.StartVoiceRecognition(device)) {
                _log.LogWarning("Voice-recognition link: {Device} declined to start", device.Name);
                return false;
            }

            _device = device;
            for (var i = 0; i < AudioSettleChecks; i++) {
                if (headset.IsAudioConnected(device)) {
                    _log.LogInformation("Voice-recognition link: audio connected after ~{Ms}ms; {AudioState}",
                        i * AudioSettleCheckPeriod, AndroidAudioRouteLog.DescribeState());
                    return true;
                }

                await Task.Delay(AudioSettleCheckPeriod, cancellationToken).ConfigureAwait(false);
            }

            _log.LogWarning("Voice-recognition link: {Device} accepted the session but never connected audio",
                device.Name);
            Stop();
            return false;
        }
        catch (Exception e) {
            _log.LogWarning(e, "Voice-recognition link: failed to start");
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        var device = _device;
        if (device == null)
            return;

        _device = null;
        try {
            var headset = _headsetSource?.Task is { IsCompletedSuccessfully: true } task ? task.Result : null;
            var isStopped = headset?.StopVoiceRecognition(device) ?? false;
            _log.LogInformation("Voice-recognition link: stopped on {Device} (accepted: {IsStopped})",
                device.Name, isStopped);
        }
        catch (Exception e) {
            _log.LogWarning(e, "Voice-recognition link: failed to stop");
        }
    }

    // Private methods

    private static BluetoothDevice? PickCar(IList<BluetoothDevice> devices)
        // A headset may be paired next to the car; the car is the one that says it is one.
        // HeadsetService makes whichever we pick the active device on start.
        => devices.FirstOrDefault(d => d.BluetoothClass?.DeviceClass
                is DeviceClass.AudioVideoCarAudio or DeviceClass.AudioVideoHandsfree)
            ?? devices.FirstOrDefault();

    private async Task<BluetoothHeadset?> GetHeadset(CancellationToken cancellationToken)
    {
        if (_adapter == null)
            return null;

        var headsetSource = _headsetSource;
        if (headsetSource == null) {
            headsetSource = TaskCompletionSourceExt.New<BluetoothHeadset?>();
            _headsetSource = headsetSource;
            _listener = new HeadsetProfileListener(headset => {
                if (headset == null) {
                    // The profile service went away; the next start binds afresh.
                    _log.LogInformation("Voice-recognition link: headset profile disconnected");
                    if (_headsetSource == headsetSource)
                        _headsetSource = null;
                }
                headsetSource.TrySetResult(headset);
            });
            if (!_adapter.GetProfileProxy(_context, _listener, ProfileType.Headset)) {
                _log.LogWarning("Voice-recognition link: couldn't bind the headset profile");
                _headsetSource = null;
                return null;
            }
        }

        try {
            return await headsetSource.Task.WaitAsync(ProxyTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            _log.LogWarning("Voice-recognition link: the headset profile didn't bind in time");
            return null;
        }
    }
}
