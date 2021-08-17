namespace ActualChat.Audio.Ebml
{
    public readonly struct Element
    {
        public static readonly Element Empty = new Element(VInt.UnknownSize(2), 0, MatroskaSpecification.UnknownDescriptor);

        public Element(VInt identifier, ulong sizeValue, ElementDescriptor descriptor)
        {
            Descriptor = descriptor;
            Identifier = identifier;
            Size = sizeValue;
        }

        public ElementDescriptor Descriptor { get; }

        public readonly VInt Identifier;

        public readonly ulong Size;

        public ElementType Type => Descriptor.Type;

        public bool IsEmpty => !Identifier.IsValidIdentifier && Size == 0 && Type == ElementType.None;

        public bool HasInvalidIdentifier => !Identifier.IsValidIdentifier;
    }
}