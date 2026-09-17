using System.Buffers.Binary;

namespace ActualChat.Audio.Ogg;

/// <summary>
/// Incremental Ogg/Opus parser: fed arbitrary byte chunks, it reassembles packets across lacing values and
/// pages, verifies page checksums, consumes the OpusHead/OpusTags headers and yields every audio packet as
/// a 20 ms <see cref="AudioFrame"/> with contiguous offsets from zero.
/// </summary>
public sealed class OggOpusReader
{
    private const int MinHeaderSize = 27;
    private const int ChecksumOffset = 22;
    private const int SegmentCountOffset = 26;
    private const int MaxLacingValue = 255;
    // RFC 6716 section 3.2.1: 48 frames of at most 1275 bytes each
    private const int MaxPacketLength = 48 * 1275;
    private const int HeaderPacketCount = 2;
    private static readonly byte[] CapturePattern = "OggS"u8.ToArray();
    private static readonly long[] FrameDurationTicks = [25_000, 50_000, 100_000, 200_000, 400_000, 600_000];

    private byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;
    private byte[] _packet = new byte[4 * 1024];
    private int _packetLength;
    private int _packetIndex;
    private readonly Queue<AudioFrame> _frames = new();

    public OpusHead? Head { get; private set; }
    public int PreSkip => Head?.PreSkip ?? 0;
    // The granule position of the last page that completed a packet: samples at 48 kHz incl. pre-skip
    public ulong GranulePosition { get; private set; }
    public int FrameCount { get; private set; }
    public bool IsEndOfStream { get; private set; }
    public bool HasPendingData => _end > _start || _packetLength > 0;

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_end + data.Length > _buffer.Length) {
            var length = _end - _start;
            if (length + data.Length > _buffer.Length)
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, length + data.Length));
            Buffer.BlockCopy(_buffer, _start, _buffer, 0, length);
            _start = 0;
            _end = length;
        }
        data.CopyTo(_buffer.AsSpan(_end));
        _end += data.Length;
    }

    public bool TryRead([NotNullWhen(true)] out AudioFrame? frame)
    {
        while (!_frames.TryDequeue(out frame))
            if (!TryReadPage())
                return false;

        return true;
    }

    public async IAsyncEnumerable<AudioFrame> ReadFrames(
        IAsyncEnumerable<byte[]> byteStream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in byteStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            Append(chunk);
            while (TryRead(out var frame))
                yield return frame;
        }
        if (HasPendingData)
            throw StandardError.Format("Ogg/Opus stream ended in the middle of a page or packet.");
    }

    public static TimeSpan GetPacketDuration(ReadOnlySpan<byte> packet)
    {
        // RFC 6716 section 3.1: the TOC byte is config (5 bits), stereo flag, frame count code (2 bits)
        if (packet.Length == 0)
            throw StandardError.Format("Empty Opus packet.");

        var toc = packet[0];
        var config = toc >> 3;
        var frameDuration = TimeSpan.FromTicks(config switch {
            < 12 => FrameDurationTicks[2 + (config & 3)], // SILK: 10, 20, 40, 60 ms
            < 16 => FrameDurationTicks[2 + (config & 1)], // Hybrid: 10, 20 ms
            _ => FrameDurationTicks[config & 3], // CELT: 2.5, 5, 10, 20 ms
        });
        var frameCount = (toc & 3) switch {
            0 => 1,
            1 or 2 => 2,
            _ => packet.Length > 1
                ? packet[1] & 0x3F
                : throw StandardError.Format("Opus packet with frame count code 3 has no frame count byte."),
        };
        return frameDuration * frameCount;
    }

    // Private methods

    private bool TryReadPage()
    {
        var available = _buffer.AsSpan(_start, _end - _start);
        if (available.Length < CapturePattern.Length)
            return false;
        if (!available.StartsWith(CapturePattern))
            throw StandardError.Format("Ogg page capture pattern expected.");
        if (available.Length < MinHeaderSize)
            return false;

        int segmentCount = available[SegmentCountOffset];
        var headerSize = MinHeaderSize + segmentCount;
        if (available.Length < headerSize)
            return false;

        var segmentTable = available[MinHeaderSize..headerSize];
        var bodySize = 0;
        foreach (var lacingValue in segmentTable)
            bodySize += lacingValue;
        var pageSize = headerSize + bodySize;
        if (available.Length < pageSize)
            return false;

        var page = available[..pageSize];
        VerifyChecksum(page);
        var version = page[4];
        if (version != 0)
            throw StandardError.Format($"Unsupported Ogg stream structure version {version}.");

        var headerType = (OggHeaderTypeFlag)page[5];
        var granulePosition = BinaryPrimitives.ReadUInt64LittleEndian(page[6..]);
        var isContinued = (headerType & OggHeaderTypeFlag.Continued) != 0;
        if ((headerType & OggHeaderTypeFlag.BeginOfStream) != 0) {
            if (_packetLength > 0)
                throw StandardError.Format("Ogg page begins a new stream, but the previous packet was left open.");

            // A new logical stream (a concatenated response part): its headers come again, frames go on
            _packetIndex = 0;
        }
        else if (!isContinued && _packetLength > 0)
            throw StandardError.Format("Ogg page continues no packet, but the previous one was left open.");
        else if (isContinued && _packetLength == 0)
            throw StandardError.Format("Ogg page continues a packet that never started.");

        var body = page[headerSize..];
        var hasCompletedPacket = false;
        foreach (var lacingValue in segmentTable) {
            AppendToPacket(body[..lacingValue]);
            body = body[lacingValue..];
            if (lacingValue == MaxLacingValue)
                continue;

            OnPacket(_packet.AsSpan(0, _packetLength));
            _packetLength = 0;
            hasCompletedPacket = true;
        }
        if (hasCompletedPacket)
            GranulePosition = granulePosition;
        IsEndOfStream = (headerType & OggHeaderTypeFlag.EndOfStream) != 0;
        _start += pageSize;
        if (_start == _end)
            _start = _end = 0;
        return true;
    }

    private static void VerifyChecksum(ReadOnlySpan<byte> page)
    {
        // The CRC covers the whole page with its own field zeroed
        var expected = BinaryPrimitives.ReadUInt32LittleEndian(page[ChecksumOffset..]);
        var crc = OggCRC32.Get(0, page[..ChecksumOffset]);
        crc = OggCRC32.Get(crc, stackalloc byte[sizeof(uint)]);
        crc = OggCRC32.Get(crc, page[(ChecksumOffset + sizeof(uint))..]);
        if (crc != expected)
            throw StandardError.Format($"Ogg page checksum mismatch: {crc:X8} computed, {expected:X8} stored.");
    }

    private void AppendToPacket(ReadOnlySpan<byte> data)
    {
        if (_packetLength + data.Length > MaxPacketLength)
            throw StandardError.Format($"Ogg packet is longer than {MaxPacketLength} bytes, the Opus maximum.");
        if (_packetLength + data.Length > _packet.Length)
            Array.Resize(ref _packet, Math.Max(_packet.Length * 2, _packetLength + data.Length));
        data.CopyTo(_packet.AsSpan(_packetLength));
        _packetLength += data.Length;
    }

    private void OnPacket(ReadOnlySpan<byte> packet)
    {
        switch (_packetIndex++) {
        case 0:
            Head = ParseHead(packet);
            break;
        case 1:
            if (packet.Length < sizeof(ulong)
                || BinaryPrimitives.ReadUInt64BigEndian(packet) != OpusTags.Signature)
                throw StandardError.Format("OpusTags packet expected.");
            break;
        default:
            var duration = GetPacketDuration(packet);
            if (duration != Constants.Audio.OpusFrameDuration)
                throw StandardError.Format(
                    $"Opus packet #{_packetIndex - HeaderPacketCount} is {duration.TotalMilliseconds} ms long, "
                    + $"only {Constants.Audio.OpusFrameDurationMs} ms single-frame packets are supported.");

            _frames.Enqueue(new AudioFrame {
                Data = packet.ToArray(),
                Offset = Constants.Audio.OpusFrameDuration * FrameCount++,
            });
            break;
        }
    }

    private static OpusHead ParseHead(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < OpusHead.Size || BinaryPrimitives.ReadUInt64BigEndian(packet) != OpusHead.Signature)
            throw StandardError.Format("OpusHead packet expected.");

        return new OpusHead {
            Version = packet[8],
            OutputChannelCount = packet[9],
            PreSkip = BinaryPrimitives.ReadUInt16LittleEndian(packet[10..]),
            InputSampleRate = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]),
            OutputGain = BinaryPrimitives.ReadInt16LittleEndian(packet[16..]),
            ChannelMapping = packet[18],
        };
    }
}
