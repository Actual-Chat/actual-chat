
namespace ActualChat.Audio.Ebml.Models
{
    public abstract class BaseModel
    {
        public static readonly BaseModel Empty = new EmptyModel();
        public abstract ElementDescriptor Descriptor { get; }
        
        [MatroskaElementDescriptor(MatroskaSpecification.Void)]
        public byte[]? Void { get; set; }

        [MatroskaElementDescriptor(MatroskaSpecification.CRC32)]
        public byte[]? CRC32 { get; set; }
    }

    public sealed class EmptyModel : BaseModel
    {
        public override ElementDescriptor Descriptor => MatroskaSpecification.UnknownDescriptor;
    }
}