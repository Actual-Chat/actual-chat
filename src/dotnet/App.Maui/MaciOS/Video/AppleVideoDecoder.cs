using CoreMedia;
using CoreVideo;
using VideoToolbox;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Wraps a <see cref="VTDecompressionSession"/> for one inbound H.264 layer. Builds
/// VideoToolbox sample buffers from Annex-B frames via <see cref="H264SampleBufferBuilder"/>
/// and raises <see cref="Decoded"/> with the decoded pixel buffer on the VT callback thread.
/// The buffer is only valid during the callback — consume it there.
/// </summary>
public sealed class AppleVideoDecoder : IDisposable
{
    private readonly ILogger _log;
    private readonly H264SampleBufferBuilder _builder;
    private readonly Lock _sync = new();
    private VTDecompressionSession? _session;

    public event Action<CVPixelBuffer>? Decoded;

    public AppleVideoDecoder(ILogger log)
    {
        _log = log;
        _builder = new H264SampleBufferBuilder(log);
    }

    public void Decode(ReadOnlyMemory<byte> annexB, bool isKeyFrame)
    {
        try {
            using var sampleBuffer = _builder.TryBuild(annexB.Span, isKeyFrame, out var formatChanged);
            if (sampleBuffer is null)
                return; // no keyframe seen yet, or no VCL NALs

            if ((formatChanged || _session is null) && !RecreateSession(_builder.Format!))
                return;

            var session = _session;
            if (session is null)
                return;

            var status = session.DecodeFrame(sampleBuffer, default, IntPtr.Zero, out _);
            if (status != VTStatus.Ok)
                _log.LogWarning("AppleVideoDecoder: DecodeFrame failed: {Status}", status);
        }
        catch (Exception e) {
            _log.LogWarning(e, "AppleVideoDecoder: decode failed");
        }
    }

    public void Dispose()
    {
        lock (_sync) {
            _session?.Dispose();
            _session = null;
        }
        _builder.Dispose();
    }

    // Private methods

    private bool RecreateSession(CMVideoFormatDescription format)
    {
        var destAttributes = new CVPixelBufferAttributes {
            PixelFormatType = CVPixelFormatType.CV32BGRA,
        };
        var session = VTDecompressionSession.Create(OnDecodedFrame, format, null, destAttributes);
        if (session is null) {
            _log.LogWarning("AppleVideoDecoder: failed to create VTDecompressionSession");
            return false;
        }

        lock (_sync) {
            _session?.Dispose();
            _session = session;
        }
        return true;
    }

    private void OnDecodedFrame(
        IntPtr sourceFrame, VTStatus status, VTDecodeInfoFlags flags,
        CVImageBuffer? imageBuffer, CMTime pts, CMTime duration)
    {
        if (status != VTStatus.Ok || imageBuffer is not CVPixelBuffer pixelBuffer)
            return;

        Decoded?.Invoke(pixelBuffer);
    }
}
