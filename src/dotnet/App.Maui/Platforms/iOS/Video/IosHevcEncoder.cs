using System.Runtime.InteropServices;
using System.Threading.Channels;
using ActualChat.Video;
using AVFoundation;
using CoreMedia;
using CoreVideo;
using VideoToolbox;

namespace ActualChat.App.Maui.Video;

public sealed class IosHevcEncoder : IDisposable
{
    private VTCompressionSession? _session;
    private Channel<VideoFrame>? _outputChannel;
    private Moment _captureStartTime;
    private bool _captureStartTimeSet;
    private readonly Lock _sync = new();
    private readonly ILogger _log;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Fps { get; private set; }
    public int Bitrate { get; private set; }

    public IosHevcEncoder(ILogger log)
        => _log = log;

    public ChannelReader<VideoFrame> Initialize(int width, int height, int fps, int bitrate)
    {
        Width = width;
        Height = height;
        Fps = fps;
        Bitrate = bitrate;

        _outputChannel = Channel.CreateBounded<VideoFrame>(
            new BoundedChannelOptions(60) {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });

        CreateSession(width, height, fps, bitrate);
        return _outputChannel.Reader;
    }

    public void EncodeSampleBuffer(CMSampleBuffer sampleBuffer)
    {
        var session = _session;
        if (session == null)
            return;

        var imageBuffer = sampleBuffer.GetImageBuffer();
        if (imageBuffer is not CVPixelBuffer pixelBuffer)
            return;

        var pts = sampleBuffer.PresentationTimeStamp;
        var duration = sampleBuffer.Duration;

        if (!_captureStartTimeSet) {
            _captureStartTime = new Moment(DateTimeOffset.UtcNow);
            _captureStartTimeSet = true;
        }

        var status = session.EncodeFrame(pixelBuffer, pts, duration, null, pixelBuffer, out _);
        if (status != VTStatus.Ok)
            _log.LogWarning("EncodeFrame returned {Status}", status);
    }

    public void Reconfigure(int width, int height, int bitrate)
    {
        lock (_sync) {
            if (width == Width && height == Height && bitrate == Bitrate)
                return;

            _log.LogInformation("Reconfigure: {OldW}x{OldH}@{OldBr} -> {NewW}x{NewH}@{NewBr}",
                Width, Height, Bitrate, width, height, bitrate);

            InvalidateSession();
            Width = width;
            Height = height;
            Bitrate = bitrate;
            CreateSession(width, height, Fps, bitrate);
        }
    }

    public void Dispose()
    {
        InvalidateSession();
        _outputChannel?.Writer.TryComplete();
    }

    private void CreateSession(int width, int height, int fps, int bitrate)
    {
        var status = VTCompressionSession.Create(
            width, height,
            CMVideoCodecType.Hevc,
            OnEncodedFrame,
            null,
            out var session);

        if (status != VTStatus.Ok || session == null) {
            _log.LogError("Failed to create VTCompressionSession: {Status}", status);
            return;
        }

        session.RealTime = true;
        session.AllowFrameReordering = false;
        session.AverageBitRate = bitrate;
        session.MaxKeyFrameInterval = fps; // 1 keyframe per second
        session.ProfileLevel = VTProfileLevel.Hevc_Main_AutoLevel;

        session.PrepareToEncodeFrames();
        _session = session;
    }

    private void InvalidateSession()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session == null)
            return;

        try {
            session.CompleteFrames(CMTime.Invalid);
        }
        catch (Exception e) {
            _log.LogWarning(e, "CompleteFrames failed during invalidation");
        }
        session.InvalidateAndClose();
        session.Dispose();
    }

    private void OnEncodedFrame(
        nint sourceFrame,
        VTStatus status,
        VTEncodeInfoFlags flags,
        CMSampleBuffer? sampleBuffer)
    {
        if (status != VTStatus.Ok || sampleBuffer == null)
            return;

        try {
            var isKeyFrame = !sampleBuffer.SampleAttachments[0].Dictionary
                .ContainsKey(CMSampleAttachmentKey.NotSync);

            var pts = sampleBuffer.PresentationTimeStamp;
            var duration = sampleBuffer.Duration;
            var offset = TimeSpan.FromSeconds(pts.Seconds) - TimeSpan.FromSeconds(0); // relative to session start

            byte[]? description = null;
            string? codec = null;
            int width = 0, height = 0;

            if (isKeyFrame) {
                codec = "hvc1";
                width = Width;
                height = Height;
                description = ExtractParameterSets(sampleBuffer);
            }

            var data = ExtractEncodedData(sampleBuffer);
            if (data == null)
                return;

            var frame = new VideoFrame(isKeyFrame) {
                Data = data,
                Offset = offset,
                Duration = duration.IsValid ? TimeSpan.FromSeconds(duration.Seconds) : TimeSpan.FromSeconds(1.0 / Fps),
                Width = width,
                Height = height,
                Description = description,
                Codec = codec,
            };

            _outputChannel?.Writer.TryWrite(frame);
        }
        catch (Exception e) {
            _log.LogError(e, "Failed to process encoded frame");
        }
    }

    private byte[]? ExtractParameterSets(CMSampleBuffer sampleBuffer)
    {
        try {
            var formatDescription = sampleBuffer.FormatDescription;
            if (formatDescription == null)
                return null;

            using var ms = new MemoryStream();
            ReadOnlySpan<byte> startCode = [0x00, 0x00, 0x00, 0x01];

            // Extract VPS, SPS, PPS (indices 0, 1, 2 for HEVC)
            for (var i = 0; i < 3; i++) {
                var paramStatus = CMFormatDescription.GetHevcParameterSet(
                    formatDescription.Handle, i,
                    out var ptr, out var size, out _, out _);
                if (paramStatus != 0 || ptr == IntPtr.Zero)
                    continue;

                ms.Write(startCode);
                var paramData = new byte[size];
                Marshal.Copy(ptr, paramData, 0, (int)size);
                ms.Write(paramData);
            }

            return ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to extract HEVC parameter sets");
            return null;
        }
    }

    private static byte[]? ExtractEncodedData(CMSampleBuffer sampleBuffer)
    {
        var blockBuffer = sampleBuffer.GetDataBuffer();
        if (blockBuffer == null)
            return null;

        var length = (int)blockBuffer.DataLength;
        var data = new byte[length];
        blockBuffer.CopyDataBytes(0, (uint)length, data);
        return data;
    }
}
