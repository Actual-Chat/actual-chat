using System.Diagnostics.CodeAnalysis;

namespace ActualChat.DependencyInjection;

public sealed class TypeMap<TScope>
{
    public Dictionary<Type, Type> Map { get; }

    public TypeMap() : this(new()) { }
    internal TypeMap(Dictionary<Type, Type> map)
        => Map = map;

    // Conversion

    public TypeMapper<TScope> ToTypeMapper()
        => new(this);

    public static implicit operator TypeMapper<TScope>(TypeMap<TScope> typeMap)
        => typeMap.ToTypeMapper();

    public TypeMap<TScope> Add<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TKey,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TValue>()
        => Add(typeof(TKey), typeof(TValue));

    public TypeMap<TScope> Add(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type key,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type value)
    {
        Map.Add(key, value);
        return this;
    }
}
