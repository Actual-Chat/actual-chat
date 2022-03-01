using ActualChat.Spans;

namespace ActualChat.Audio.WebM.Models;

/// <summary>
///     http://matroska.sourceforge.net/technical/specs/index.html#block_structure
/// </summary>
public class Block : BaseModel, IParseRawBinary
{
    private const byte LacingBits = 0b0000110;
    private const byte InvisibleBit = 0b00010000;

    public override EbmlElementDescriptor Descriptor => MatroskaSpecification.BlockDescriptor;

    public ulong TrackNumber { get;  set; }
    public byte Flags { get; protected set; }

    /// <summary>
    ///     Number of frames in the lace-1 (uint8)
    /// </summary>
    public int NumFrames { get; set; }

    /// <summary>
    ///     Lace-coded size of each frame of the lace, except for the last one (multiple uint8).
    ///     *This is not used with Fixed-size lacing as it is calculated automatically from (total size of lace) / (number of
    ///     frames in lace).
    /// </summary>
    public byte LaceCodedSizeOfEachFrame { get; set; }
    public short TimeCode { get; set; }
    public bool IsInvisible  {
        get => (Flags & InvisibleBit) == InvisibleBit;
        set => Flags = (byte)(value
            ? Flags | InvisibleBit
            : Flags & ~InvisibleBit);
    }

    public Lacing Lacing {
        get => (Lacing)(Flags & LacingBits);
        set => Flags = (byte)((byte)(Flags & ~LacingBits) | ((byte)value & LacingBits));
    }

    // TODO(AK): [OPTIMIZE] Try to get rid of byte array and replace with e.g. Memory<byte>
    public byte[]? Data { get; set; }

    public virtual void Parse(ReadOnlySpan<byte> span)
    {
        var spanReader = new SpanReader(span);

        TrackNumber = spanReader.ReadVInt()!.Value.Value;
        TimeCode = spanReader.ReadShort()!.Value;
        Flags = spanReader.ReadByte()!.Value;

        IsInvisible = (Flags & InvisibleBit) == InvisibleBit;
        Lacing = (Lacing)(Flags & LacingBits);

        if (Lacing != Lacing.No) {
            NumFrames = spanReader.ReadByte()!.Value;

            if (Lacing != Lacing.FixedSize)
                LaceCodedSizeOfEachFrame = spanReader.ReadByte()!.Value;
        }

        Data = span[spanReader.Position..].ToArray();
    }

    public override ulong GetSize()
    {
        var size = 0UL;
        size += EbmlHelper.GetSize(TrackNumber);
        size += 2;
        size += 1;
        size += (ulong?)Data?.Length ?? 0UL;

        return size;
    }

    public virtual bool Write(ref SpanWriter writer)
    {
        if (!EbmlHelper.WriteEbmlMasterElement(MatroskaSpecification.Block, GetSize(), ref writer))
            return false;

        writer.Write(VInt.EncodeSize(TrackNumber));
        writer.Write(TimeCode);
        writer.Write(Flags);
        writer.Write(Data);
        return true;
    }
}
