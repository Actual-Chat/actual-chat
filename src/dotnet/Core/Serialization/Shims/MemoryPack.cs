// ReSharper disable once CheckNamespace
namespace MemoryPack;

#pragma warning disable CA1019

public enum GenerateType
{
    Object = 0,
    VersionTolerant = 1,
    CircularReference = 2,
    Collection = 3,
    NoGenerate = 4,
}

public enum SerializeLayout { Sequential = 0, Explicit = 1 }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class MemoryPackableAttribute : Attribute
{
    public MemoryPackableAttribute() { }
    public MemoryPackableAttribute(GenerateType generateType) { }
    public MemoryPackableAttribute(SerializeLayout serializeLayout) { }
    public MemoryPackableAttribute(GenerateType generateType, SerializeLayout serializeLayout) { }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MemoryPackOrderAttribute : Attribute
{
    public MemoryPackOrderAttribute(int order) { }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MemoryPackIgnoreAttribute : Attribute;

[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method)]
public sealed class MemoryPackConstructorAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MemoryPackIncludeAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MemoryPackAllowSerializeAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MemoryPackUnionAttribute : Attribute
{
    public MemoryPackUnionAttribute(ushort tag, Type type) { }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class MemoryPackUnionFormatterAttribute : Attribute
{
    public MemoryPackUnionFormatterAttribute(Type formatterTargetType) { }
}
