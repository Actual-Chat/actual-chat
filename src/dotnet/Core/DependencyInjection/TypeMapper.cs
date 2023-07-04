namespace ActualChat.DependencyInjection;

public class TypeMapper<TScope>
{
    private readonly IReadOnlyDictionary<Type, Type> _map;
    private readonly ConcurrentDictionary<Type, Type?> _cache = new();

    public TypeMapper(IServiceProvider services)
        => _map = services.GetServices<TypeMap<TScope>>()
            .SelectMany(r => r.Map)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    public TypeMapper(TypeMap<TScope> map)
        => _map = map.Map;

    public TypeMapper(IReadOnlyDictionary<Type, Type> map)
        => _map = map;

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
            var baseTypes = source1.GetAllBaseTypes(true, true);
            foreach (var cType in baseTypes) {
                var match = self._map.GetValueOrDefault(cType);
                if (match != null) {
                    if (match.IsGenericTypeDefinition)
                        throw Stl.Internal.Errors.GenericMatchForConcreteType(cType, match);
                    if (!match.IsAssignableTo(typeof(TScope)))
                        throw Stl.Internal.Errors.MustBeAssignableTo<TScope>(match);
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
                    throw Stl.Internal.Errors.ConcreteMatchForGenericType(gType, match);

                #pragma warning disable IL2055
                match = match.MakeGenericType(gTypeArgs);
                #pragma warning restore IL2055

                if (!match.IsAssignableTo(typeof(TScope)))
                    throw Stl.Internal.Errors.MustBeAssignableTo<TScope>(match);
                return match;
            }
            return null;
        }, this);
    }
}
