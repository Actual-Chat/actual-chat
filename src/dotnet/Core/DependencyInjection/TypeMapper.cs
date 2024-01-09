namespace ActualChat.DependencyInjection;

public sealed class TypeMapper<TScope>
{
    private readonly Dictionary<Type, Type> _map;
    private readonly ConcurrentDictionary<Type, Type?> _cache = new();

    public TypeMapper(TypeMap<TScope> typeMap)
        => _map = typeMap.Map;

    public TypeMapper(Action<TypeMap<TScope>> typeMapBuilder)
    {
        var typeMap = new TypeMap<TScope>();
        typeMapBuilder.Invoke(typeMap);
        _map = typeMap.Map;
    }

    public TypeMapper(IServiceProvider services)
        : this(new TypeMap<TScope>(services.GetServices<TypeMap<TScope>>()
            .SelectMany(r => r.Map)
            .ToDictionary(kv => kv.Key, kv => kv.Value)))
    { }

    public Type Get(Type source)
        => TryGet(source)
            ?? throw StandardError.NotFound<Type>(
                $"No matching {typeof(TScope).GetName()} is found for type '{source.GetName()}'.");

    public Type? TryGet(Type source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        return _cache.GetOrAdd(source, static (key, self) => {
            var source1 = key;
#pragma warning disable IL2067
            var baseTypes = source1.GetAllBaseTypes(true, true);
#pragma warning restore IL2067
            foreach (var cType in baseTypes) {
                var match = self._map.GetValueOrDefault(cType);
                if (match != null) {
                    if (match.IsGenericTypeDefinition)
                        throw ActualLab.Internal.Errors.GenericMatchForConcreteType(cType, match);
                    if (!match.IsAssignableTo(typeof(TScope)))
                        throw ActualLab.Internal.Errors.MustBeAssignableTo<TScope>(match);
                    return match;
                }
                if (!cType.IsConstructedGenericType)
                    continue;

                var gType = cType.GetGenericTypeDefinition();
                var gTypeArgs = cType.GetGenericArguments();
                match = self._map.GetValueOrDefault(gType);
                if (match == null)
                    continue;

                if (!match.IsGenericTypeDefinition)
                    throw ActualLab.Internal.Errors.ConcreteMatchForGenericType(gType, match);

#pragma warning disable IL2055
                match = match.MakeGenericType(gTypeArgs);
#pragma warning restore IL2055

                if (!match.IsAssignableTo(typeof(TScope)))
                    throw ActualLab.Internal.Errors.MustBeAssignableTo<TScope>(match);
                return match;
            }
            return null;
        }, this);
    }
}
