using System.Runtime.CompilerServices;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Video;
using AVFoundation;
using CoreMedia;
using CoreVideo;
using Foundation;
using UIKit;

namespace ActualChat.App.Maui.Video;

public sealed class IosVideoCapture : NSObject, INativeVideoCapture, IAVCaptureVideoDataOutputSampleBufferDelegate
{
    private AVCaptureSession? _session;
    private IosHevcEncoder? _encoder;
    private AVCaptureVideoPreviewLayer? _previewLayer;
    private IosBackgroundBlur? _blur;
    private volatile bool _isBlurEnabled;
    private readonly ILogger _log;

    public int CaptureWidth { get; private set; }
    public int CaptureHeight { get; private set; }

    public IosVideoCapture(AppUIHub hub)
        => _log = hub.LogFor<IosVideoCapture>();

    public Task<IAsyncEnumerable<VideoFrame>> StartCapture(VideoCaptureConfig config, CancellationToken ct)
    {
        _log.LogInformation("StartCapture: {Width}x{Height} @ {Fps}fps, {Bitrate}bps, front={Front}",
            config.Width, config.Height, config.Fps, config.Bitrate, config.UseFrontCamera);

        var session = new AVCaptureSession();
        session.SessionPreset = config.Width >= 1920 ? AVCaptureSession.Preset1920x1080 : AVCaptureSession.Preset1280x720;

        // Camera input
        var cameraPosition = config.UseFrontCamera ? AVCaptureDevicePosition.Front : AVCaptureDevicePosition.Back;
        var camera = AVCaptureDevice.GetDefaultDevice(
            AVCaptureDeviceType.BuiltInWideAngleCamera,
            AVMediaTypes.Video.GetConstant()!,
            cameraPosition);

        if (camera == null) {
            _log.LogError("No camera found for position {Position}", cameraPosition);
            throw new InvalidOperationException($"No camera found for position {cameraPosition}");
        }

        // Configure camera frame rate
        camera.LockForConfiguration(out var error);
        if (error == null) {
            camera.ActiveVideoMinFrameDuration = new CMTime(1, config.Fps);
            camera.ActiveVideoMaxFrameDuration = new CMTime(1, config.Fps);
            camera.UnlockForConfiguration();
        }

        var input = AVCaptureDeviceInput.FromDevice(camera, out error);
        if (input == null || error != null) {
            _log.LogError("Failed to create camera input: {Error}", error?.LocalizedDescription);
            throw new InvalidOperationException($"Failed to create camera input: {error?.LocalizedDescription}");
        }

        if (!session.CanAddInput(input)) {
            _log.LogError("Cannot add camera input to session");
            throw new InvalidOperationException("Cannot add camera input to session");
        }
        session.AddInput(input);

        // Video data output
        var output = new AVCaptureVideoDataOutput {
            AlwaysDiscardsLateVideoFrames = true,
        };
        output.WeakVideoSettings = new NSDictionary(
            CVPixelBuffer.PixelFormatTypeKey,
            new NSNumber((uint)CVPixelFormatType.CV420YpCbCr8BiPlanarFullRange));

        var queue = new CoreFoundation.DispatchQueue("ios-video-capture", false);
        output.SetSampleBufferDelegate(this, queue);

        if (!session.CanAddOutput(output)) {
            _log.LogError("Cannot add video output to session");
            throw new InvalidOperationException("Cannot add video output to session");
        }
        session.AddOutput(output);

        // Mirror front camera
        var connection = output.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant()!);
        if (connection != null && connection.SupportsVideoMirroring && config.UseFrontCamera)
            connection.VideoMirrored = true;

        // Read actual camera capture dimensions
        var dimensions = ((CMVideoFormatDescription)camera.ActiveFormat.FormatDescription).Dimensions;
        CaptureWidth = (int)dimensions.Width;
        CaptureHeight = (int)dimensions.Height;
        _log.LogInformation("StartCapture: actual camera dimensions {W}x{H}", CaptureWidth, CaptureHeight);

        // Cap encoder dimensions to actual camera capabilities
        var encoderWidth = Math.Min(config.Width, CaptureWidth);
        var encoderHeight = Math.Min(config.Height, CaptureHeight);
        var encoderBitrate = config.Bitrate;
        if (encoderWidth != config.Width || encoderHeight != config.Height) {
            var ratio = (double)(encoderWidth * encoderHeight) / (config.Width * config.Height);
            encoderBitrate = (int)(encoderBitrate * ratio);
            _log.LogInformation("StartCapture: capped to {W}x{H} @ {Br}bps", encoderWidth, encoderHeight, encoderBitrate);
        }

        // Create encoder
        _encoder = new IosHevcEncoder(_log);
        var reader = _encoder.Initialize(encoderWidth, encoderHeight, config.Fps, encoderBitrate);

        // Start session
        _session = session;
        session.StartRunning();

        _log.LogInformation("StartCapture: session started");

        return Task.FromResult(ReadFrames(reader, ct));
    }

    public Task StopCapture()
    {
        _log.LogInformation("StopCapture");

        HidePreview();

        var session = Interlocked.Exchange(ref _session, null);
        if (session != null) {
            session.StopRunning();
            session.Dispose();
        }

        var encoder = Interlocked.Exchange(ref _encoder, null);
        encoder?.Dispose();

        var blur = Interlocked.Exchange(ref _blur, null);
        blur?.Dispose();

        return Task.CompletedTask;
    }

    public void SetBackgroundBlur(bool enabled)
    {
        _isBlurEnabled = enabled;
        if (enabled && _blur == null)
            _blur = new IosBackgroundBlur(_log);
        // Don't dispose on disable — keep warm for quick re-enable
        // Dispose happens in StopCapture
    }

    public void Reconfigure(int width, int height, int bitrate)
    {
        var session = _session;
        var encoder = _encoder;
        if (session == null || encoder == null)
            return;

        // Cap to actual camera capabilities
        var cappedWidth = Math.Min(width, CaptureWidth);
        var cappedHeight = Math.Min(height, CaptureHeight);
        if (cappedWidth != width || cappedHeight != height) {
            var ratio = (double)(cappedWidth * cappedHeight) / (width * height);
            bitrate = (int)(bitrate * ratio);
            width = cappedWidth;
            height = cappedHeight;
        }

        session.BeginConfiguration();
        try {
            if (width >= 1920)
                session.SessionPreset = AVCaptureSession.Preset1920x1080;
            else if (width >= 1280)
                session.SessionPreset = AVCaptureSession.Preset1280x720;
            else
                session.SessionPreset = AVCaptureSession.PresetMedium;

            encoder.Reconfigure(width, height, bitrate);
        }
        finally {
            session.CommitConfiguration();
        }
    }

    public void ShowPreview()
    {
        var session = _session;
        if (session == null)
            return;

        HidePreview();

        var previewLayer = new AVCaptureVideoPreviewLayer(session) {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = UIScreen.MainScreen.Bounds,
        };

        var hostView = GetWebViewNativeView();
        if (hostView == null) {
            _log.LogWarning("ShowPreview: Could not find WebView native view");
            return;
        }

        hostView.Layer.AddSublayer(previewLayer);
        _previewLayer = previewLayer;
        _log.LogInformation("ShowPreview: Preview layer added");
    }

    public void HidePreview()
    {
        var layer = Interlocked.Exchange(ref _previewLayer, null);
        if (layer == null)
            return;

        layer.RemoveFromSuperLayer();
        layer.Dispose();
        _log.LogInformation("HidePreview: Preview layer removed");
    }

    // IAVCaptureVideoDataOutputSampleBufferDelegate
    [Export("captureOutput:didOutputSampleBuffer:fromConnection:")]
    public void DidOutputSampleBuffer(AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
    {
        var encoder = _encoder;
        if (encoder == null)
            return;

        if (_isBlurEnabled && _blur is { } blur) {
            var imageBuffer = sampleBuffer.GetImageBuffer();
            if (imageBuffer is CVPixelBuffer inputBuffer) {
                var blurred = blur.Process(inputBuffer);
                if (blurred != null) {
                    encoder.EncodePixelBuffer(blurred, sampleBuffer.PresentationTimeStamp, sampleBuffer.Duration);
                    return;
                }
            }
            // Fallback: blur failed, encode original
        }
        encoder.EncodeSampleBuffer(sampleBuffer);
    }

    private static UIView? GetWebViewNativeView()
    {
        var window = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);

        return window?.RootViewController?.View;
    }

    private static async IAsyncEnumerable<VideoFrame> ReadFrames(
        System.Threading.Channels.ChannelReader<VideoFrame> reader,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var frame in reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return frame;
    }
}
