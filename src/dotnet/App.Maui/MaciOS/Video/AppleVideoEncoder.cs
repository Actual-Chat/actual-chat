using System.Runtime.InteropServices;
using CoreFoundation;
using CoreMedia;
using CoreVideo;
using Foundation;
using VideoToolbox;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// One encoded per-layer frame produced by <see cref="AppleVideoEncoder"/>.
/// <see cref="Data"/> is AVCC/HVCC length-prefixed NAL units (VideoToolbox default);
/// <see cref="Description"/> is the avcC/hvcC atom, present on keyframes only.
/// </summary>
public sealed record NativeEncodedFrame
{
    public required byte[] Data { get; init; }
    public required bool IsKeyFrame { get; init; }
    public byte[]? Description { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required CMTime Pts { get; init; }
    public required byte LayerId { get; init; }
}

/// <summary>
/// Wraps a single <see cref="VTCompressionSession"/> for one simulcast layer.
/// Feeds it full-resolution <c>CVPixelBuffer</c>s (VideoToolbox scales to the layer's
/// coded dims) and raises <see cref="Encoded"/> per output chunk on the VT callback thread.
/// </summary>
public sealed class AppleVideoEncoder : IDisposable
{
    private const string AvcCAtomKey = "avcC";
    private const string HvcCAtomKey = "hvcC";
    private const string AtomsExtensionKey = "SampleDescriptionExtensionAtoms";

    private static readonly byte[] StartCode = [0, 0, 0, 1];

    private readonly ILogger _log;
    private readonly byte _layerId;
    private readonly bool _isHevc;
    private VTCompressionSession? _session;
    private byte[]? _parameterSetsAnnexB;

    public event Action<NativeEncodedFrame>? Encoded;

    public int Width { get; }
    public int Height { get; }

    public AppleVideoEncoder(byte layerId, int width, int height, int bitrate, double frameRate, string codec, ILogger log)
    {
        _log = log;
        _layerId = layerId;
        Width = width;
        Height = height;
        _isHevc = codec.StartsWith("hev1", StringComparison.OrdinalIgnoreCase)
            || codec.StartsWith("hvc1", StringComparison.OrdinalIgnoreCase);

        var codecType = _isHevc ? CMVideoCodecType.Hevc : CMVideoCodecType.H264;
        // Low-latency rate control produces a real-time bitstream whose SPS signals
        // no frame reordering (max_num_reorder_frames = 0). Without it, the SPS
        // implies the level's full DPB, so the receiver's WebCodecs decoder buffers
        // several frames before emitting the first — which stalls the live player
        // (hang → recovery loop) and shows a black tile.
        using var encoderSpec = new NSMutableDictionary {
            { new NSString("EnableLowLatencyRateControl"), NSNumber.FromBoolean(true) },
        };
        var session = VTCompressionSession.Create(
            width,
            height,
            codecType,
            OnEncodedFrame,
            new VTVideoEncoderSpecification(encoderSpec),
            (NSDictionary?)null);
        if (session is null)
            throw StandardError.External($"Failed to create VTCompressionSession for layer {layerId} ({width}x{height}).");

        var properties = new VTCompressionProperties {
            RealTime = true,
            AllowFrameReordering = false,
            // Emit each frame with no reorder/output delay so the receiver's
            // WebCodecs decoder outputs frames as they arrive instead of holding
            // them for a (non-existent) reorder window — otherwise it never emits
            // in the streaming (no-flush) path and stalls into hang→recovery.
            MaxFrameDelayCount = 0,
            AverageBitRate = bitrate,
            ExpectedFrameRate = frameRate,
            // We drive keyframes manually via ForceKeyFrame; keep the auto interval
            // large so the encoder doesn't insert unrequested IDRs.
            MaxKeyFrameInterval = 100_000,
            MaxKeyFrameIntervalDuration = 100_000,
        };
        if (!_isHevc)
            properties.ProfileLevel = VTProfileLevel.H264HighAutoLevel;
        session.SetCompressionProperties(properties);
        session.PrepareToEncodeFrames();
        _session = session;
    }

    public void Encode(CVImageBuffer imageBuffer, CMTime pts, CMTime duration, bool forceKeyFrame)
    {
        var session = _session;
        if (session is null)
            return;

        NSDictionary? frameProperties = null;
        if (forceKeyFrame)
            frameProperties = new NSDictionary(VTEncodeFrameOptionKey.ForceKeyFrame, NSNumber.FromBoolean(true));

        var status = session.EncodeFrame(imageBuffer, pts, duration, frameProperties, IntPtr.Zero, out _);
        if (status != VTStatus.Ok)
            _log.LogWarning("AppleVideoEncoder L{Layer}: EncodeFrame failed: {Status}", _layerId, status);
    }

    public void Dispose()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is null)
            return;

        session.CompleteFrames(CMTime.Invalid);
        session.Dispose();
    }

    // Private methods

    private void OnEncodedFrame(IntPtr sourceFrame, VTStatus status, VTEncodeInfoFlags flags, CMSampleBuffer? sampleBuffer)
    {
        if (status != VTStatus.Ok || sampleBuffer is null)
            return;

        try {
            if (!sampleBuffer.IsValid || sampleBuffer.NumSamples == 0)
                return;

            var isKeyFrame = IsKeyFrame(sampleBuffer);
            var avccData = CopyData(sampleBuffer);
            if (avccData is null)
                return;

            // The receiver decodes H.264 as Annex-B (start-code framed, SPS/PPS
            // inline on keyframes) with no VideoDecoderConfig.description — the same
            // convention the JS/WebCodecs sender uses. VideoToolbox emits AVCC
            // (length-prefixed, parameter sets only in the format description), so
            // convert here and inline the parameter sets on every keyframe.
            if (isKeyFrame && ExtractDescription(sampleBuffer) is { } avcC)
                _parameterSetsAnnexB = BuildParameterSetsAnnexB(avcC);
            var data = ToAnnexB(avccData, isKeyFrame ? _parameterSetsAnnexB : null);

            var frame = new NativeEncodedFrame {
                Data = data,
                IsKeyFrame = isKeyFrame,
                Description = null,
                Width = Width,
                Height = Height,
                Pts = sampleBuffer.PresentationTimeStamp,
                LayerId = _layerId,
            };
            Encoded?.Invoke(frame);
        }
        catch (Exception e) {
            _log.LogError(e, "AppleVideoEncoder L{Layer}: failed to process encoded frame", _layerId);
        }
    }

    private static bool IsKeyFrame(CMSampleBuffer sampleBuffer)
    {
        var attachments = sampleBuffer.GetSampleAttachments(false);
        if (attachments is null || attachments.Length == 0)
            return true;

        // NotSync == true → this sample depends on others (not a keyframe).
        return attachments[0].NotSync != true;
    }

    private byte[]? CopyData(CMSampleBuffer sampleBuffer)
    {
        using var block = sampleBuffer.GetDataBuffer();
        if (block is null)
            return null;

        var dataPointer = IntPtr.Zero;
        var error = block.GetDataPointer(0, out _, out var totalLength, ref dataPointer);
        if (error != CMBlockBufferError.None || dataPointer == IntPtr.Zero || totalLength == 0)
            return null;

        var length = (int)totalLength;
        var data = new byte[length];
        Marshal.Copy(dataPointer, data, 0, length);
        return data;
    }

    private byte[]? ExtractDescription(CMSampleBuffer sampleBuffer)
    {
        if (sampleBuffer.GetVideoFormatDescription() is not { } format)
            return null;

        if (format.GetExtension(AtomsExtensionKey) is not NSDictionary atoms)
            return null;

        var key = _isHevc ? HvcCAtomKey : AvcCAtomKey;
        return atoms[key] is NSData atom ? atom.ToArray() : null;
    }

    // Rewrites AVCC (4-byte big-endian length prefixes) to Annex-B (00 00 00 01
    // start codes), optionally prepending the cached parameter sets on keyframes.
    private static byte[] ToAnnexB(byte[] avcc, byte[]? parameterSetsPrefix)
    {
        using var stream = new MemoryStream(avcc.Length + (parameterSetsPrefix?.Length ?? 0) + 16);
        if (parameterSetsPrefix is not null)
            stream.Write(parameterSetsPrefix, 0, parameterSetsPrefix.Length);
        var offset = 0;
        while (offset + 4 <= avcc.Length) {
            var nalLength = (avcc[offset] << 24) | (avcc[offset + 1] << 16) | (avcc[offset + 2] << 8) | avcc[offset + 3];
            offset += 4;
            if (nalLength <= 0 || offset + nalLength > avcc.Length)
                break;
            stream.Write(StartCode, 0, StartCode.Length);
            stream.Write(avcc, offset, nalLength);
            offset += nalLength;
        }
        return stream.ToArray();
    }

    // Parses the avcC box's SPS/PPS NAL units and returns them Annex-B framed.
    private static byte[]? BuildParameterSetsAnnexB(byte[] avcC)
    {
        // avcC: [0]=version, [1..3]=profile/compat/level, [4]=lengthSizeMinusOne,
        // [5]=(0xE0 | numSPS), then per SPS: uint16 length + bytes; then numPPS + per PPS.
        if (avcC.Length < 6)
            return null;

        using var stream = new MemoryStream(avcC.Length + 16);
        var offset = 5;
        // SPS: offset is at the (0xE0 | numSPS) byte; WriteParameterSets skips it.
        if (!WriteParameterSets(avcC, ref offset, avcC[offset] & 0x1F, stream))
            return null;
        // PPS: offset now points at the numPPS byte.
        if (offset >= avcC.Length || !WriteParameterSets(avcC, ref offset, avcC[offset], stream))
            return null;
        return stream.ToArray();
    }

    // On entry offset points at the count byte (skipped here); on exit it points
    // at the byte following the last parameter set.
    private static bool WriteParameterSets(byte[] avcC, ref int offset, int count, MemoryStream stream)
    {
        offset++;
        for (var i = 0; i < count; i++) {
            if (offset + 2 > avcC.Length)
                return false;
            var length = (avcC[offset] << 8) | avcC[offset + 1];
            offset += 2;
            if (length <= 0 || offset + length > avcC.Length)
                return false;
            stream.Write(StartCode, 0, StartCode.Length);
            stream.Write(avcC, offset, length);
            offset += length;
        }
        return true;
    }
}
