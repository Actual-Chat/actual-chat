using ActualChat.Spans;

namespace ActualChat.Audio.Ogg;

[StructLayout(LayoutKind.Sequential)]
public ref struct OggOpusWriter(OggOpusWriter.State state, Span<byte> span)
{
    private const ulong SamplesPerFrame = 960;
    private SpanWriter _spanWriter = new (span);

    public ReadOnlySpan<byte> Written => _spanWriter.Span[.._spanWriter.Position];
    public int Position => _spanWriter.Position;

    public bool Write(OpusHead opusHead)
    {
        state.SerialNumber = Random.Shared.Next();
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
        _spanWriter.Write(header.GranulePosition);
        _spanWriter.Write(header.StreamSerialNumber);
        _spanWriter.Write(header.PageSequenceNumber);
        var checksumPosition = Position;
        _spanWriter.Write(header.PageChecksum);
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
        _spanWriter.Write(crc, checksumPosition);
        return true;
    }

    // ReSharper disable once UnusedParameter.Global
    public bool Write(OpusTags opusTags)
    {
        var header = new OggHeader {
            HeaderType = OggHeaderTypeFlag.Continued,
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
        _spanWriter.Write(header.GranulePosition);
        _spanWriter.Write(header.StreamSerialNumber);
        _spanWriter.Write(header.PageSequenceNumber);
        var checksumPosition = Position;
        _spanWriter.Write(header.PageChecksum);
        _spanWriter.Write(header.PageSegmentCount);
        _spanWriter.Write(header.SegmentTable);

        _spanWriter.Write(OpusTags.Signature);
        _spanWriter.Write(OpusTags.VendorStringLength, isLittleEndian: true);
        _spanWriter.Write(OpusTags.VendorString);

        var crc = OggCRC32.Get(0, _spanWriter.Span[..Position]);
        _spanWriter.Write(crc, checksumPosition);
        return true;
    }

    public bool Write(IReadOnlyCollection<AudioFrame> audioFrames)
    {
        var granulePosition = state.GranulePosition + ((uint)audioFrames.Count * SamplesPerFrame);
        state.GranulePosition = granulePosition;
        var segmentTable = BuildSegmentTable(audioFrames);
        var header = new OggHeader {
            HeaderType = 0,
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
        _spanWriter.Write(header.GranulePosition);
        _spanWriter.Write(header.StreamSerialNumber);
        _spanWriter.Write(header.PageSequenceNumber);
        var checksumPosition = Position;
        _spanWriter.Write(header.PageChecksum);
        _spanWriter.Write(header.PageSegmentCount);
        _spanWriter.Write(header.SegmentTable);


        foreach (var audioFrame in audioFrames)
            _spanWriter.Write(audioFrame.Data);

        var crc = OggCRC32.Get(0, _spanWriter.Span[..Position]);
        _spanWriter.Write(crc, checksumPosition);
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
        public int SerialNumber { get; set; }
    }
}
