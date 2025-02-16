namespace ActualChat.DependencyInjection;

public sealed class TypeMapper<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]TScope>
{
    private readonly Dictionary<Type, Type> _map;
    private readonly ConcurrentDictionary<Type, LazySlim<Type, TypeMapper<TScope>, Type?>> _cache = new();

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

    [UnconditionalSuppressMessage("Trimming", "IL2073:NoMatchingAnnotation", Justification = "Type is marked with DynamicallyAccessedMembers.")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type Get([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]Type source)
        => TryGet(source)
            ?? throw StandardError.NotFound<Type>(
                $"No matching {typeof(TScope).GetName()} is found for type '{source.GetName()}'.");

    public Type? TryGet([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]Type source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "Covered by DynamicallyAccessedMemberTypes.All above")]
        [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Covered by DynamicallyAccessedMemberTypes.All above")]
        static Type? MappedTypeFactory(Type key, TypeMapper<TScope> self) {
            var source1 = key;
            var baseTypes = source1.GetAllBaseTypes(true, true);
            foreach (var cType in baseTypes) {
                var match = self._map.GetValueOrDefault(cType);
                if (match != null) {
                    if (match.IsGenericTypeDefinition)
                        throw StandardError.Internal($"A generic match '{match.GetName()}' is found for non-generic type '{cType.GetName()}'.");
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
                    throw StandardError.Internal($"A non-generic match '{match.GetName()}' is found for generic type '{gType.GetName()}'.");

                match = match.MakeGenericType(gTypeArgs);
                if (!match.IsAssignableTo(typeof(TScope)))
                    throw ActualLab.Internal.Errors.MustBeAssignableTo<TScope>(match);

                return match;
            }
            return null;
        }

        return _cache.GetOrAdd(source, MappedTypeFactory, this);
    }
}
