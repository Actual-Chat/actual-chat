namespace ActualChat.Audio.WebM;

public readonly struct EbmlElement
{
    public static readonly EbmlElement Empty = new EbmlElement(VInt.UnknownSize(2), 0, MatroskaSpecification.UnknownDescriptor);

    public EbmlElement(VInt identifier, ulong sizeValue, EbmlElementDescriptor descriptor)
    {
        Descriptor = descriptor;
        Identifier = identifier;
        Size = sizeValue;
    }

    public EbmlElementDescriptor Descriptor { get; }

    public readonly VInt Identifier;

    public readonly ulong Size;

    public EbmlElementType Type => Descriptor.Type;

    public bool IsEmpty => !Identifier.IsValidIdentifier && Size == 0 && Type == EbmlElementType.None;

    public bool HasInvalidIdentifier => !Identifier.IsValidIdentifier;
}
