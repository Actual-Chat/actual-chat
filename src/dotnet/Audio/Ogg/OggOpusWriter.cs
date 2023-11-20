using ActualChat.Spans;

namespace ActualChat.Audio.Ogg;

[StructLayout(LayoutKind.Sequential)]
public ref struct OggOpusWriter(OggOpusWriter.State state, Span<byte> span)
{
    private const ulong SamplesPerMillisecond = 48;
    private SpanWriter _spanWriter = new (span);

    public ReadOnlySpan<byte> Written => _spanWriter.Span[.._spanWriter.Position];
    public int Position => _spanWriter.Position;

    public bool Write(OpusHead opusHead)
    {
        var header = new OggHeader {
            HeaderType = OggHeaderTypeFlag.BeginOfStream,
            GranulePosition = 0,
            StreamSerialNumber = state.SerialNumber,
            PageSequenceNumber = state.PageCount++,
            PageSegmentCount = 1,
            SegmentTable = new [] { OpusHead.Size },
        };

        if (_spanWriter.Position + header.Size + OpusHead.Size > _spanWriter.Length)
            return false;

        _spanWriter.Write(OggHeader.CapturePattern);
        _spanWriter.Write(header.StreamStructureVersion);
        _spanWriter.Write((byte)header.HeaderType);
        _spanWriter.Write(header.GranulePosition, isLittleEndian: true);
        _spanWriter.Write(header.StreamSerialNumber);
        _spanWriter.Write(header.PageSequenceNumber, isLittleEndian: true);
        var checksumPosition = Position;
        _spanWriter.Write(header.PageChecksum, isLittleEndian: true);
        _spanWriter.Write(header.PageSegmentCount);
        _spanWriter.Write(header.SegmentTable);

        _spanWriter.Write(OpusHead.Signature);
        _spanWriter.Write(opusHead.Version);
        _spanWriter.Write(opusHead.OutputChannelCount);
        _spanWriter.Write(opusHead.PreSkip, isLittleEndian: true);
        _spanWriter.Write(opusHead.InputSampleRate, isLittleEndian: true);
        _spanWriter.Write(opusHead.OutputGain, isLittleEndian: true);
        _spanWriter.Write(opusHead.ChannelMapping);

        var crc = OggCRC32.Get(0, _spanWriter.Span[..Position]);
        _spanWriter.Write(crc, checksumPosition, isLittleEndian: true);
        return true;
    }

    // ReSharper disable once UnusedParameter.Global
    public bool Write(OpusTags opusTags)
    {
        var header = new OggHeader {
            HeaderType = 0,
            GranulePosition = 0,
            StreamSerialNumber = state.SerialNumber,
            PageSequenceNumber = state.PageCount++,
            PageSegmentCount = 1,
            SegmentTable = new [] { OpusTags.Size },
        };
        if (_spanWriter.Position + header.Size + OpusTags.Size > _spanWriter.Length)
            return false;

        _spanWriter.Write(OggHeader.CapturePattern);
        _spanWriter.Write(header.StreamStructureVersion);
        _spanWriter.Write((byte)header.HeaderType);
        _spanWriter.Write(header.GranulePosition, isLittleEndian: true);
        _spanWriter.Write(header.StreamSerialNumber);
        _spanWriter.Write(header.PageSequenceNumber, isLittleEndian: true);
        var checksumPosition = Position;
        _spanWriter.Write(header.PageChecksum, isLittleEndian: true);
        _spanWriter.Write(header.PageSegmentCount);
        _spanWriter.Write(header.SegmentTable);

        _spanWriter.Write(OpusTags.Signature);
        _spanWriter.Write(OpusTags.VendorStringLength, isLittleEndian: true);
        _spanWriter.Write(OpusTags.VendorString);
        _spanWriter.Write(OpusTags.UserCommentListLength, isLittleEndian: true);

        var crc = OggCRC32.Get(0, _spanWriter.Span[..Position]);
        _spanWriter.Write(crc, checksumPosition, isLittleEndian: true);
        return true;
    }

    public bool Write(IReadOnlyCollection<AudioFrame> audioFrames, bool hasNext)
    {
        // var granulePosition = state.GranulePosition + ((uint)audioFrames.Count * SamplesPerFrame);
        var granulePosition = state.GranulePosition + ((ulong)audioFrames.Sum(f => f.Duration.Ticks) * SamplesPerMillisecond / 10_000ul);
        state.GranulePosition = granulePosition;
        var segmentTable = BuildSegmentTable(audioFrames);
        var header = new OggHeader {
            HeaderType = hasNext ? 0 : OggHeaderTypeFlag.EndOfStream,
            GranulePosition = granulePosition ,
            StreamSerialNumber = state.SerialNumber,
            PageSequenceNumber = state.PageCount++,
            PageSegmentCount = (byte)segmentTable.Length,
            SegmentTable = segmentTable,
        };
        if (_spanWriter.Position + header.Size + OpusTags.Size > _spanWriter.Length)
            return false;

        _spanWriter.Write(OggHeader.CapturePattern);
        _spanWriter.Write(header.StreamStructureVersion);
        _spanWriter.Write((byte)header.HeaderType);
        _spanWriter.Write(header.GranulePosition, isLittleEndian: true);
        _spanWriter.Write(header.StreamSerialNumber);
        _spanWriter.Write(header.PageSequenceNumber, isLittleEndian: true);
        var checksumPosition = Position;
        _spanWriter.Write(header.PageChecksum, isLittleEndian: true);
        _spanWriter.Write(header.PageSegmentCount);
        _spanWriter.Write(header.SegmentTable);


        foreach (var audioFrame in audioFrames)
            _spanWriter.Write(audioFrame.Data);

        var crc = OggCRC32.Get(0, _spanWriter.Span[..Position]);
        _spanWriter.Write(crc, checksumPosition, isLittleEndian: true);
        return true;

        static byte[] BuildSegmentTable(IReadOnlyCollection<AudioFrame> audioFrames)
        {
            var result = new List<byte>(audioFrames.Count);
            foreach (var audioFrame in audioFrames) {
                var length = audioFrame.Data.Length;
                while (length >= 255) {
                    length -= 255;
                    result.Add(255);
                }
                result.Add((byte)length);
            }
            return result.ToArray();
        }
    }

    public class State
    {
        public int PageCount { get; set; }
        public ulong GranulePosition { get; set; }
        public uint SerialNumber { get; set; }
    }
}
