using CoreMedia;
using CoreVideo;
using VideoToolbox;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Wraps a <see cref="VTDecompressionSession"/> for one inbound H.264 layer. Accepts
/// Annex-B frames (start-code framed, SPS/PPS inline on keyframes — the receiver wire
/// convention shared by the JS and native senders), rebuilds them as AVCC for
/// VideoToolbox, and raises <see cref="Decoded"/> with the decoded pixel buffer on the
/// VT callback thread. The buffer is only valid during the callback — consume it there.
/// </summary>
public sealed class AppleVideoDecoder : IDisposable
{
    private readonly ILogger _log;
    private readonly Lock _sync = new();
    private VTDecompressionSession? _session;
    private CMVideoFormatDescription? _formatDescription;
    private byte[]? _sps;
    private byte[]? _pps;

    public event Action<CVPixelBuffer>? Decoded;

    public AppleVideoDecoder(ILogger log)
        => _log = log;

    public void Decode(byte[] annexB, bool isKeyFrame)
    {
        try {
            if (isKeyFrame && !TryConfigure(annexB))
                return;

            var session = _session;
            var format = _formatDescription;
            if (session is null || format is null)
                return; // no keyframe seen yet

            var avcc = ToAvcc(annexB);
            if (avcc.Length == 0)
                return;

            DecodeAvcc(session, format, avcc);
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
            _formatDescription?.Dispose();
            _formatDescription = null;
        }
    }

    // Private methods

    private bool TryConfigure(byte[] annexB)
    {
        byte[]? sps = null, pps = null;
        foreach (var (offset, length) in EnumerateNals(annexB)) {
            var nalType = annexB[offset] & 0x1F;
            if (nalType == 7)
                sps = annexB.AsSpan(offset, length).ToArray();
            else if (nalType == 8)
                pps = annexB.AsSpan(offset, length).ToArray();
        }
        if (sps is null || pps is null)
            return _session is not null; // keyframe without inline params: reuse the live session

        if (_session is not null && _sps is not null && _pps is not null
            && sps.AsSpan().SequenceEqual(_sps) && pps.AsSpan().SequenceEqual(_pps))
            return true;

        var format = CMVideoFormatDescription.FromH264ParameterSets([sps, pps], 4, out var formatError);
        if (format is null || formatError != CMFormatDescriptionError.None) {
            _log.LogWarning("AppleVideoDecoder: FromH264ParameterSets failed: {Error}", formatError);
            return false;
        }

        var destAttributes = new CVPixelBufferAttributes {
            PixelFormatType = CVPixelFormatType.CV32BGRA,
        };
        var session = VTDecompressionSession.Create(OnDecodedFrame, format, null, destAttributes);
        if (session is null) {
            _log.LogWarning("AppleVideoDecoder: failed to create VTDecompressionSession");
            format.Dispose();
            return false;
        }

        lock (_sync) {
            _session?.Dispose();
            _formatDescription?.Dispose();
            _session = session;
            _formatDescription = format;
            _sps = sps;
            _pps = pps;
        }
        return true;
    }

    private void DecodeAvcc(VTDecompressionSession session, CMVideoFormatDescription format, byte[] avcc)
    {
        using var blockBuffer = CMBlockBuffer.FromMemoryBlock(avcc, 0, CMBlockBufferFlags.AssureMemoryNow, out var bbError);
        if (blockBuffer is null || bbError != CMBlockBufferError.None) {
            _log.LogWarning("AppleVideoDecoder: CMBlockBuffer creation failed: {Error}", bbError);
            return;
        }

        var sampleSizes = new nuint[] { (nuint)avcc.Length };
        using var sampleBuffer = CMSampleBuffer.CreateReady(blockBuffer, format, 1, null, sampleSizes, out var sbError);
        if (sampleBuffer is null || sbError != CMSampleBufferError.None) {
            _log.LogWarning("AppleVideoDecoder: CMSampleBuffer creation failed: {Error}", sbError);
            return;
        }

        var status = session.DecodeFrame(sampleBuffer, default, IntPtr.Zero, out _);
        if (status != VTStatus.Ok)
            _log.LogWarning("AppleVideoDecoder: DecodeFrame failed: {Status}", status);
    }

    private void OnDecodedFrame(
        IntPtr sourceFrame, VTStatus status, VTDecodeInfoFlags flags,
        CVImageBuffer? imageBuffer, CMTime pts, CMTime duration)
    {
        if (status != VTStatus.Ok || imageBuffer is not CVPixelBuffer pixelBuffer)
            return;

        Decoded?.Invoke(pixelBuffer);
    }

    // Length-prefixes every VCL NAL (dropping the parameter sets that went into the
    // format description) — the inverse of AppleVideoEncoder.ToAnnexB.
    private static byte[] ToAvcc(byte[] annexB)
    {
        using var stream = new MemoryStream(annexB.Length + 16);
        foreach (var (offset, length) in EnumerateNals(annexB)) {
            var nalType = annexB[offset] & 0x1F;
            if (nalType == 7 || nalType == 8)
                continue;
            stream.WriteByte((byte)((length >> 24) & 0xFF));
            stream.WriteByte((byte)((length >> 16) & 0xFF));
            stream.WriteByte((byte)((length >> 8) & 0xFF));
            stream.WriteByte((byte)(length & 0xFF));
            stream.Write(annexB, offset, length);
        }
        return stream.ToArray();
    }

    // Yields (payloadOffset, payloadLength) for each Annex-B NAL, handling both 3- and
    // 4-byte start codes.
    private static List<(int Offset, int Length)> EnumerateNals(byte[] data)
    {
        var marks = new List<(int StartCode, int Payload)>();
        var n = data.Length;
        var i = 0;
        while (i + 3 <= n) {
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1) {
                marks.Add((i, i + 3));
                i += 3;
            }
            else if (i + 4 <= n && data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1) {
                marks.Add((i, i + 4));
                i += 4;
            }
            else
                i++;
        }

        var result = new List<(int, int)>(marks.Count);
        for (var m = 0; m < marks.Count; m++) {
            var payload = marks[m].Payload;
            var end = m + 1 < marks.Count ? marks[m + 1].StartCode : n;
            if (end > payload)
                result.Add((payload, end - payload));
        }
        return result;
    }
}
