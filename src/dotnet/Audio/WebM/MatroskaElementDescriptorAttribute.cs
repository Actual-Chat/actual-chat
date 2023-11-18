namespace ActualChat.Audio.WebM;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class MatroskaElementDescriptorAttribute(ulong identifier, Type? elementType = null) : Attribute
{
    public ulong Identifier { get; } = identifier;
    public Type? ElementType { get; } = elementType;
}
