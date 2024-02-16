using ActualChat.Spans;

namespace ActualChat.Audio.WebM.Models;

public sealed class EncryptedBlock : Block
{
    public override EbmlElementDescriptor Descriptor => MatroskaSpecification.EncryptedBlockDescriptor;

    public override bool Write(ref SpanWriter writer)
    {
        if (!EbmlHelper.WriteEbmlMasterElement(MatroskaSpecification.EncryptedBlock, GetSize(), ref writer))
            return false;

        writer.Write(VInt.EncodeSize(TrackNumber));
        writer.Write(TimeCode);
        writer.Write(Flags);
        writer.Write(Data);
        return true;
    }
}
