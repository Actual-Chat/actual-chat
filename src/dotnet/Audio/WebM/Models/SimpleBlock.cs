using ActualChat.Spans;

namespace ActualChat.Audio.WebM.Models;

/// <summary>
///     http://matroska.sourceforge.net/technical/specs/index.html#simpleblock_structure
/// </summary>
public sealed class SimpleBlock : Block
{
    private const byte KeyFrameBit = 0b10000000;
    private const byte DiscardableBit = 0b00000001;

    public override EbmlElementDescriptor Descriptor => MatroskaSpecification.SimpleBlockDescriptor;

    public bool IsKeyFrame {
        get => (Flags & KeyFrameBit) == KeyFrameBit;
        set => Flags = (byte)(value
            ? Flags | KeyFrameBit
            : Flags & ~KeyFrameBit);
    }

    public bool IsDiscardable {
        get => (Flags & DiscardableBit) == DiscardableBit;
        set => Flags = (byte)(value
            ? Flags | DiscardableBit
            : Flags & ~DiscardableBit);
    }

    public override bool Write(ref SpanWriter writer)
    {
        if (!EbmlHelper.WriteEbmlMasterElement(MatroskaSpecification.SimpleBlock, GetSize(), ref writer))
            return false;

        writer.Write(VInt.EncodeSize(TrackNumber));
        writer.Write(TimeCode);
        writer.Write(Flags);
        writer.Write(Data);
        return true;
    }
}
