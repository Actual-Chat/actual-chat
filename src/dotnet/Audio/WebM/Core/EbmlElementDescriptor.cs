using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Audio.WebM;

public class EbmlElementDescriptor
{
    public EbmlElementDescriptor(ulong identifier, string name, EbmlElementType type, string? defaultvalue, bool listEntry)
        : this(VInt.FromEncoded(identifier), name, type, defaultvalue, listEntry)
    { }

    public EbmlElementDescriptor(long identifier, string name, EbmlElementType type, string? defaultValue, bool listEntry)
        : this(VInt.FromEncoded((ulong)identifier), name, type, defaultValue, listEntry)
    { }

    private EbmlElementDescriptor(VInt identifier, string name, EbmlElementType type,string? defaultValue, bool listEntry)
    {
        if (!identifier.IsValidIdentifier && type != EbmlElementType.None)
            throw new ArgumentException("Value is not valid identifier.", nameof(identifier));

        Identifier = identifier;
        Name = name;
        Type = type;
        DefaultValue = defaultValue;
        ListEntry = listEntry;
    }

    public bool ListEntry { get; }

    public VInt Identifier { get; }

    public string? Name { get; }

    public EbmlElementType Type { get; }

    public string? DefaultValue { get; }

    public override int GetHashCode()
    {
        var result = 17;
        result = 37*result + Identifier.GetHashCode();
        result = 37*result + (Name == null ? 0 : Name.OrdinalHashCode());
        result = 37*result + (Type == EbmlElementType.None ? 0 : Type.GetHashCode());
        return result;
    }

    public override bool Equals(object? obj)
    {
        if (this == obj) return true;

        if (obj is EbmlElementDescriptor o2)
            return Identifier.Equals(o2.Identifier)
                && Equals(Name, o2.Name)
                && Type == o2.Type;
        return false;
    }

    [SuppressMessage("ReSharper", "HeapView.BoxingAllocation")]
    public override string ToString()
        => $"{Name}:{Type} - id:{Identifier}";

    public EbmlElementDescriptor Named(string name)
        => new(Identifier, name, Type, DefaultValue, ListEntry);
}
