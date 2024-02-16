using ActualChat.Spans;

namespace ActualChat.Audio.WebM.Models;

public sealed class BlockAdditional : Block
{
    public override EbmlElementDescriptor Descriptor => MatroskaSpecification.BlockAdditionalDescriptor;

    public override bool Write(ref SpanWriter writer)
    {
        if (!EbmlHelper.WriteEbmlMasterElement(MatroskaSpecification.BlockAdditional, GetSize(), ref writer))
            return false;

        writer.Write(VInt.EncodeSize(TrackNumber));
        writer.Write(TimeCode);
        writer.Write(Flags);
        writer.Write(Data);
        return true;
    }
}
