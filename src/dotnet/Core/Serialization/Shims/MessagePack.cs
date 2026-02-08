// ReSharper disable once CheckNamespace
namespace MessagePack;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class MessagePackObjectAttribute : Attribute
{
    public MessagePackObjectAttribute() { }
    public MessagePackObjectAttribute(bool keyAsPropertyName) => KeyAsPropertyName = keyAsPropertyName;
    public bool KeyAsPropertyName { get; }
    public bool SuppressSourceGeneration { get; set; }
    public bool AllowPrivate { get; set; }
}

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
    AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field |
    AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
public sealed class MessagePackFormatterAttribute(Type formatterType) : Attribute
{
    public Type FormatterType { get; } = formatterType;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class KeyAttribute : Attribute
{
    public KeyAttribute(int x) { }
    public KeyAttribute(string x) { }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class IgnoreMemberAttribute : Attribute;

[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method)]
public sealed class SerializationConstructorAttribute : Attribute;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = true)]
public sealed class UnionAttribute(int key, Type subType) : Attribute
{
    public int Key { get; } = key;
    public Type SubType { get; } = subType;
}
