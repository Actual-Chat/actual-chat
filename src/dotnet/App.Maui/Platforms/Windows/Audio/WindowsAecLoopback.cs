using NAudio.CoreAudioApi;
using NAudio.Wave;
using Role = NAudio.CoreAudioApi.Role;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// The APM's reverse stream: a WASAPI loopback of the render endpoint our playback goes to.
/// It moves when that endpoint changes and restarts after a failure.
/// </summary>
internal sealed class WindowsAecLoopback : IDisposable
{
    private const int MaxRestartCount = 3;
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(1);
    private static string? _playbackDeviceId;

    private readonly Lock _lock = new();
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private readonly BlockRingBuffer<float> _buffer;
    private readonly int _bufferMs;
    private DefaultDeviceWatcher? _deviceWatcher;
    private WasapiCapture? _capture;
    private MMDevice? _device;
    private int _restartCount;
    private bool _isDisposed;

    private ILogger Log { get; }

    private static event Action? PlaybackDeviceChanged;

    public WindowsAecLoopback(
        MMDeviceEnumerator deviceEnumerator,
        BlockRingBuffer<float> buffer,
        int bufferMs,
        ILogger log)
    {
        _deviceEnumerator = deviceEnumerator;
        _buffer = buffer;
        _bufferMs = bufferMs;
        Log = log;
        PlaybackDeviceChanged += OnPlaybackDeviceChanged;
        try {
            _deviceWatcher = new DefaultDeviceWatcher(DataFlow.Render, null, OnDefaultRenderDeviceChanged);
            deviceEnumerator.RegisterEndpointNotificationCallback(_deviceWatcher);
        }
        catch (Exception e) {
            // Losing it costs only the move to a new default output
            Log.LogWarning(e, "Can't watch for default render device changes");
            _deviceWatcher = null;
        }
    }

    public void Dispose()
    {
        PlaybackDeviceChanged -= OnPlaybackDeviceChanged;
        if (_deviceWatcher is not null) {
            try {
                _deviceEnumerator.UnregisterEndpointNotificationCallback(_deviceWatcher);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to stop watching default render device changes");
            }
            _deviceWatcher = null;
        }
        lock (_lock) {
            _isDisposed = true;
            StopUnsafe();
        }
    }

    public static void ReportPlaybackDevice(string? deviceId)
    {
        // The WinRT id of the endpoint a playback graph opened - with per-app output routing
        // it isn't the default one, and a loopback of the default would hear none of our audio.
        if (deviceId.IsNullOrEmpty() || Interlocked.Exchange(ref _playbackDeviceId, deviceId) == deviceId)
            return;

        PlaybackDeviceChanged?.Invoke();
    }

    public bool Start()
    {
        lock (_lock)
            return TryStartUnsafe();
    }

    // Private methods

    private void OnDefaultRenderDeviceChanged()
    {
        // A default-device playback graph follows the new default, and the id it reported is the
        // old one. The next graph reports again; until then the default is the better guess.
        Interlocked.Exchange(ref _playbackDeviceId, null);
        Restart("default render device changed", TimeSpan.Zero);
    }

    private void OnPlaybackDeviceChanged()
        => Restart("playback device changed", TimeSpan.Zero);

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Our own StopRecording raises this too, with no exception
        if (e.Exception is not { } error)
            return;

        Log.LogWarning(error, "AEC loopback capture stopped - echo cancellation is degraded");
        if (Interlocked.Increment(ref _restartCount) > MaxRestartCount) {
            Log.LogError("AEC loopback failed {Count} times, giving up until the next recording", MaxRestartCount);
            return;
        }

        Restart("loopback failed", RestartDelay * _restartCount);
    }

    private void Restart(string reason, TimeSpan delay)
        // Off the notifying thread: WASAPI callbacks must not block, and a restart opens a client
        => _ = BackgroundTask.Run(async () => {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay).ConfigureAwait(false);
            lock (_lock) {
                if (_isDisposed)
                    return;

                Log.LogInformation("Restarting the AEC loopback: {Reason}", reason);
                TryStartUnsafe();
            }
        }, CancellationToken.None);

    private bool TryStartUnsafe()
    {
        StopUnsafe();
        try {
            _device = GetRenderDevice();
            var capture = new CustomWasapiLoopbackCapture(_device, true, _bufferMs) {
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Constants.Audio.RecordingSampleRate, 1),
            };
            capture.DataAvailable += OnDataAvailable;
            // NAudio's only channel for a capture-thread failure, e.g. the render endpoint going away
            capture.RecordingStopped += OnRecordingStopped;
            _capture = capture;
            // WASAPI Initialize happens here, so an app holding the endpoint exclusively fails it
            capture.StartRecording();
            Log.LogInformation("AEC loopback started on '{Device}'", _device.FriendlyName);
            return true;
        }
        catch (Exception e) {
            Log.LogWarning(e, "AEC loopback failed to start; continuing without it");
            StopUnsafe();
            return false;
        }
    }

    private void StopUnsafe()
    {
        // Dispose joins the capture thread, so no DataAvailable runs past it
        if (_capture is { } capture) {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            capture.DisposeSilently();
            _capture = null;
        }
        _device.DisposeSilently();
        _device = null;
    }

    private MMDevice GetRenderDevice()
    {
        var playbackDeviceId = Volatile.Read(ref _playbackDeviceId);
        if (!playbackDeviceId.IsNullOrEmpty())
            foreach (var device in _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
                // WinRT ids embed the WASAPI endpoint id
                if (playbackDeviceId.Contains(device.ID, StringComparison.OrdinalIgnoreCase))
                    return device;

                device.Dispose();
            }
        return _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (args.BytesRecorded == 0 || sender is not WasapiCapture capture)
            return;

        WindowsAudioCapture.PushToBuffer(args, capture.WaveFormat, _buffer);
    }
}
