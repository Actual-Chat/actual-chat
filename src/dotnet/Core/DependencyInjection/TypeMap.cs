namespace ActualChat.DependencyInjection;

public sealed record TypeMap<TScope>(IReadOnlyDictionary<Type, Type> Map)
{
    public TypeMap() : this(ImmutableDictionary<Type, Type>.Empty) { }
    public TypeMap(Action<Dictionary<Type, Type>>? builder) : this(UseBuilder(builder)) { }

    // Conversion

    public TypeMapper<TScope> ToTypeMapper()
        => new(this);

    public static implicit operator TypeMapper<TScope>(TypeMap<TScope> typeMap)
        => typeMap.ToTypeMapper();

    // Private methods

    private static IReadOnlyDictionary<Type, Type> UseBuilder(Action<Dictionary<Type, Type>>? builder)
    {
        if (builder == null)
            return ImmutableDictionary<Type, Type>.Empty;

        var map = new Dictionary<Type, Type>();
        builder.Invoke(map);
        return map;
    }
}
