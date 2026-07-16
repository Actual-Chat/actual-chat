using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using CoreMedia;
using CoreVideo;
using Foundation;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// AVFoundation camera capture for Apple platforms (iOS + Mac Catalyst). Delivers raw
/// NV12 <see cref="CMSampleBuffer"/> frames synchronously via <see cref="FrameCaptured"/>,
/// so the consumer must encode inside the callback before the buffer is disposed.
/// </summary>
public sealed class AppleVideoCapture : IDisposable
{
    private readonly ILogger _log;
    private readonly DispatchQueue _captureQueue = new("actual.video.capture");
    private readonly Lock _sync = new();

    private AVCaptureSession? _session;
    private AVCaptureDeviceInput? _input;
    private AVCaptureVideoDataOutput? _output;
    private SampleDelegate? _delegate;
    private AVCaptureDevice? _device;

    public event Action<CMSampleBuffer, CMTime>? FrameCaptured;

    public string? CurrentDeviceId { get; private set; }
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }

    public AppleVideoCapture(ILogger log)
        => _log = log;

    public bool Start(string? deviceId)
    {
        lock (_sync) {
            if (_session is not null)
                return true;

            var authStatus = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
            if (authStatus == AVAuthorizationStatus.NotDetermined)
                AVCaptureDevice.RequestAccessForMediaType(AVAuthorizationMediaType.Video, _ => { });
            else if (authStatus is AVAuthorizationStatus.Denied or AVAuthorizationStatus.Restricted) {
                _log.LogWarning("AppleVideoCapture: camera access not authorized ({Status})", authStatus);
                return false;
            }

            var device = ResolveDevice(deviceId) ?? ResolveDevice(null);
            if (device is null) {
                _log.LogWarning("AppleVideoCapture: no camera device found");
                return false;
            }

            var session = new AVCaptureSession();
            session.BeginConfiguration();
            try {
                if (session.CanSetSessionPreset(AVCaptureSession.PresetHigh))
                    session.SessionPreset = AVCaptureSession.PresetHigh;

                var input = AVCaptureDeviceInput.FromDevice(device, out var inputError);
                if (input is null || inputError is not null) {
                    _log.LogWarning(
                        "AppleVideoCapture: cannot open device input: {Error}",
                        inputError?.LocalizedDescription);
                    session.CommitConfiguration();
                    return false;
                }
                if (!session.CanAddInput(input)) {
                    _log.LogWarning("AppleVideoCapture: cannot add device input");
                    session.CommitConfiguration();
                    return false;
                }
                session.AddInput(input);

                var output = new AVCaptureVideoDataOutput {
                    AlwaysDiscardsLateVideoFrames = true,
                    WeakVideoSettings = new AVVideoSettingsUncompressed {
                        PixelFormatType = CVPixelFormatType.CV420YpCbCr8BiPlanarVideoRange,
                    }.Dictionary,
                };
                var sampleDelegate = new SampleDelegate(this);
                output.SetSampleBufferDelegate(sampleDelegate, _captureQueue);
                if (!session.CanAddOutput(output)) {
                    _log.LogWarning("AppleVideoCapture: cannot add video output");
                    session.CommitConfiguration();
                    return false;
                }
                session.AddOutput(output);

                _session = session;
                _input = input;
                _output = output;
                _delegate = sampleDelegate;
                _device = device;
                CurrentDeviceId = device.UniqueID;
                (SourceWidth, SourceHeight) = GetDimensions(device);
            }
            finally {
                session.CommitConfiguration();
            }

            _session.StartRunning();
            _log.LogInformation("AppleVideoCapture: started on {Device} ({W}x{H})",
                _device?.LocalizedName, SourceWidth, SourceHeight);
            return true;
        }
    }

    public bool SwitchTo(string? deviceId)
    {
        lock (_sync) {
            if (_session is null)
                return Start(deviceId);

            var device = ResolveDevice(deviceId);
            if (device is null || device.UniqueID == CurrentDeviceId)
                return false;

            var input = AVCaptureDeviceInput.FromDevice(device, out var inputError);
            if (input is null || inputError is not null) {
                _log.LogWarning(
                    "AppleVideoCapture: cannot open device input on switch: {Error}",
                    inputError?.LocalizedDescription);
                return false;
            }

            _session.BeginConfiguration();
            try {
                if (_input is not null)
                    _session.RemoveInput(_input);
                if (!_session.CanAddInput(input)) {
                    if (_input is not null)
                        _session.AddInput(_input);
                    return false;
                }
                _session.AddInput(input);
                _input = input;
                _device = device;
                CurrentDeviceId = device.UniqueID;
                (SourceWidth, SourceHeight) = GetDimensions(device);
            }
            finally {
                _session.CommitConfiguration();
            }
            return true;
        }
    }

    public void Stop()
    {
        lock (_sync) {
            if (_session is null)
                return;

            if (_session.Running)
                _session.StopRunning();
            if (_output is not null && _delegate is not null)
                _output.SetSampleBufferDelegate(null, (DispatchQueue?)null);
            _session.Dispose();
            _input?.Dispose();
            _output?.Dispose();
            _delegate?.Dispose();
            _session = null;
            _input = null;
            _output = null;
            _delegate = null;
            _device = null;
            CurrentDeviceId = null;
            _log.LogInformation("AppleVideoCapture: stopped");
        }
    }

    public void Dispose()
    {
        Stop();
        _captureQueue.Dispose();
    }

    // Private methods

    private static AVCaptureDevice? ResolveDevice(string? deviceId)
    {
        var devices = AppleVideoDevices.All();
        if (devices.Length == 0)
            return AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
        if (deviceId.IsNullOrEmpty())
            return devices.FirstOrDefault(d => d.Position == AVCaptureDevicePosition.Front) ?? devices[0];

        return devices.FirstOrDefault(d => d.UniqueID == deviceId) ?? devices[0];
    }

    private static (int, int) GetDimensions(AVCaptureDevice device)
    {
        var format = device.ActiveFormat;
        if (format?.FormatDescription is CMVideoFormatDescription videoFormat) {
            var dims = videoFormat.Dimensions;
            return (dims.Width, dims.Height);
        }
        return (1280, 720);
    }

    private void OnSample(CMSampleBuffer sampleBuffer)
    {
        var handler = FrameCaptured;
        if (handler is null)
            return;

        var pts = sampleBuffer.PresentationTimeStamp;
        handler(sampleBuffer, pts);
    }

    // Nested types

    private sealed class SampleDelegate(AppleVideoCapture owner) : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        public override void DidOutputSampleBuffer(
            AVCaptureOutput captureOutput,
            CMSampleBuffer sampleBuffer,
            AVCaptureConnection connection)
        {
            try {
                owner.OnSample(sampleBuffer);
            }
            finally {
                sampleBuffer.Dispose();
            }
        }
    }
}
