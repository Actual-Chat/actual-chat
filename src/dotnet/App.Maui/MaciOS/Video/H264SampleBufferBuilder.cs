using CoreMedia;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Turns inbound Annex-B H.264 frames (start-code framed, SPS/PPS inline on keyframes —
/// the receiver wire convention) into VideoToolbox-ready AVCC <see cref="CMSampleBuffer"/>s,
/// latching the parameter sets into a <see cref="CMVideoFormatDescription"/>. Shared by the
/// decode path (<see cref="AppleVideoDecoder"/>) and the display-layer path
/// (<see cref="SampleBufferDisplayView"/>). Not thread-safe: drive it from one thread.
/// </summary>
public sealed class H264SampleBufferBuilder : IDisposable
{
    private readonly ILogger _log;
    private CMVideoFormatDescription? _format;
    private byte[]? _sps;
    private byte[]? _pps;

    public CMVideoFormatDescription? Format => _format;

    public H264SampleBufferBuilder(ILogger log)
        => _log = log;

    public CMSampleBuffer? TryBuild(ReadOnlySpan<byte> annexB, bool isKeyFrame, out bool formatChanged)
    {
        // formatChanged is true only when this frame's parameter sets replaced the
        // previous ones — consumers caching decode state (a session, a display layer)
        // must reset at that boundary. Returns null before the first keyframe or for a
        // frame with no VCL NALs.
        formatChanged = false;
        if (isKeyFrame)
            formatChanged = TryLatchFormat(annexB);
        var format = _format;
        if (format is null)
            return null;

        var avcc = ToAvcc(annexB);
        if (avcc.Length == 0)
            return null;

        return CreateSampleBuffer(format, avcc);
    }

    public void Dispose()
    {
        _format?.Dispose();
        _format = null;
    }

    // Private methods

    private bool TryLatchFormat(ReadOnlySpan<byte> annexB)
    {
        byte[]? sps = null, pps = null;
        foreach (var (offset, length) in EnumerateNals(annexB)) {
            var nalType = annexB[offset] & 0x1F;
            if (nalType == 7)
                sps = annexB.Slice(offset, length).ToArray();
            else if (nalType == 8)
                pps = annexB.Slice(offset, length).ToArray();
        }
        if (sps is null || pps is null)
            return false; // keyframe without inline params: reuse the latched format

        if (_format is not null && _sps is not null && _pps is not null
            && sps.AsSpan().SequenceEqual(_sps) && pps.AsSpan().SequenceEqual(_pps))
            return false;

        var format = CMVideoFormatDescription.FromH264ParameterSets([sps, pps], 4, out var formatError);
        if (format is null || formatError != CMFormatDescriptionError.None) {
            _log.LogWarning("H264SampleBufferBuilder: FromH264ParameterSets failed: {Error}", formatError);
            return false;
        }

        _format?.Dispose();
        _format = format;
        _sps = sps;
        _pps = pps;
        return true;
    }

    private CMSampleBuffer? CreateSampleBuffer(CMVideoFormatDescription format, byte[] avcc)
    {
        using var blockBuffer = CMBlockBuffer.FromMemoryBlock(
            avcc, 0, CMBlockBufferFlags.AssureMemoryNow, out var bbError);
        if (blockBuffer is null || bbError != CMBlockBufferError.None) {
            _log.LogWarning("H264SampleBufferBuilder: CMBlockBuffer creation failed: {Error}", bbError);
            return null;
        }

        var sampleSizes = new nuint[] { (nuint)avcc.Length };
        var sampleBuffer = CMSampleBuffer.CreateReady(blockBuffer, format, 1, null, sampleSizes, out var sbError);
        if (sampleBuffer is null || sbError != CMSampleBufferError.None) {
            _log.LogWarning("H264SampleBufferBuilder: CMSampleBuffer creation failed: {Error}", sbError);
            return null;
        }
        return sampleBuffer;
    }

    private static byte[] ToAvcc(ReadOnlySpan<byte> annexB)
    {
        // Length-prefixes every VCL NAL (dropping the parameter sets that went into
        // the format description) — the inverse of AppleVideoEncoder.ToAnnexB.
        using var stream = new MemoryStream(annexB.Length + 16);
        foreach (var (offset, length) in EnumerateNals(annexB)) {
            var nalType = annexB[offset] & 0x1F;
            if (nalType == 7 || nalType == 8)
                continue;

            stream.WriteByte((byte)((length >> 24) & 0xFF));
            stream.WriteByte((byte)((length >> 16) & 0xFF));
            stream.WriteByte((byte)((length >> 8) & 0xFF));
            stream.WriteByte((byte)(length & 0xFF));
            stream.Write(annexB.Slice(offset, length));
        }
        return stream.ToArray();
    }

    private static List<(int Offset, int Length)> EnumerateNals(ReadOnlySpan<byte> data)
    {
        // Yields (payloadOffset, payloadLength) for each Annex-B NAL, handling both
        // 3- and 4-byte start codes.
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
