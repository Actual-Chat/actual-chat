namespace ActualChat.Audio.WebM.Models;

public abstract class BaseModel
{
    public static readonly BaseModel Empty = new EmptyModel();
    public abstract EbmlElementDescriptor Descriptor { get; }

    [MatroskaElementDescriptor(MatroskaSpecification.CRC32)]
    // ReSharper disable once InconsistentNaming
    public byte[]? CRC32 { get; set; }

    public abstract ulong GetSize();
}

public sealed class EmptyModel : BaseModel
{
    public override EbmlElementDescriptor Descriptor => MatroskaSpecification.UnknownDescriptor;
    public override ulong GetSize() => 0UL;
}
