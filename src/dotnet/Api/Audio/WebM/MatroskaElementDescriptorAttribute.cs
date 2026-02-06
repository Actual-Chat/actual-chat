namespace ActualChat.Audio.WebM;

/// <summary>
/// Maps a property or class to a Matroska element identifier.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class MatroskaElementDescriptorAttribute(ulong identifier, Type? elementType = null) : Attribute
{
    public ulong Identifier { get; } = identifier;
    public Type? ElementType { get; } = elementType;
}
