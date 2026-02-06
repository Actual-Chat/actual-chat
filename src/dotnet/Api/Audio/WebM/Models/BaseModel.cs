namespace ActualChat.Audio.WebM.Models;

/// <summary>
/// Base class for WebM/Matroska model elements.
/// </summary>
public abstract class BaseModel
{
    public static readonly BaseModel Empty = new EmptyModel();
    public abstract EbmlElementDescriptor Descriptor { get; }

    [MatroskaElementDescriptor(MatroskaSpecification.CRC32)]
    // ReSharper disable once InconsistentNaming
    public byte[]? CRC32 { get; set; }

    public abstract ulong GetSize();
}

/// <summary>
/// Represents an empty WebM model element.
/// </summary>
public sealed class EmptyModel : BaseModel
{
    public override EbmlElementDescriptor Descriptor => MatroskaSpecification.UnknownDescriptor;
    public override ulong GetSize() => 0UL;
}
