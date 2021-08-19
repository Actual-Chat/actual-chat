using System.IO;

namespace ActualChat.Audio.Ebml.Models
{
    public sealed class EncryptedBlock : Block
    {
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
}