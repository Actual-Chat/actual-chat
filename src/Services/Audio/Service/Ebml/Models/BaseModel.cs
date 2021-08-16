
namespace ActualChat.Audio.Ebml.Models
{
    public abstract class BaseModel
    {
        public static readonly BaseModel Empty = new EmptyModel();
        
        [MatroskaElementDescriptor(MatroskaSpecification.Void)]
        public byte[]? Void { get; set; }

        [MatroskaElementDescriptor(MatroskaSpecification.CRC32)]
        public byte[]? CRC32 { get; set; }
    }

    public sealed class EmptyModel : BaseModel
    {
        
    }
}