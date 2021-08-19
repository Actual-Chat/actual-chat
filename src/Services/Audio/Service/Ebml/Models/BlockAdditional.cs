using System.IO;

namespace ActualChat.Audio.Ebml.Models
{
    public sealed class BlockAdditional : Block
    {
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
}