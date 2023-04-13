using Stl.Extensibility;

namespace ActualChat.Hosting;

public sealed class StaticMatchingTypeFinder : IMatchingTypeFinder
{
    private readonly Dictionary<(Type Source, Symbol Scope), Type> _matches;
    private readonly ConcurrentDictionary<(Type Source, Symbol Scope), Type?> _cache = new();

    public StaticMatchingTypeFinder(IServiceProvider services)
        => _matches = Initialize(services);

    public Type? TryFind(Type source, Symbol scope)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        return _cache.GetOrAdd((source, scope), (key, self) => {
            var (source1, scope1) = key;
            var baseTypes = source1.GetAllBaseTypes(true, true);
            foreach (var cType in baseTypes) {
                var match = self._matches.GetValueOrDefault((cType, scope1));
                if (match != null) {
                    if (match.IsGenericTypeDefinition)
                        throw Stl.Internal.Errors.GenericMatchForConcreteType(cType, match);
                    return match;
                }
                if (!cType.IsConstructedGenericType)
                    continue;

                var gType = cType.GetGenericTypeDefinition();
                var gTypeArgs = cType.GetGenericArguments();
                match = self._matches.GetValueOrDefault((gType, scope1));
                if (match == null)
                    continue;

                if (!match.IsGenericTypeDefinition)
                    throw Stl.Internal.Errors.ConcreteMatchForGenericType(gType, match);

                #pragma warning disable IL2055
                match = match.MakeGenericType(gTypeArgs);
                #pragma warning restore IL2055

                return match;
            }
            return null;
        }, this);
    }

    private static Dictionary<(Type Source, Symbol Scope), Type> Initialize(IServiceProvider services)
    {
        var result = new Dictionary<(Type Source, Symbol Scope), Type>();
        var pairs = services.GetServices<IMatchingTypeRegistry>()
            .SelectMany(r => r.GetMatchedTypes());
        foreach (var pair in pairs)
            result[pair.Key] = pair.Value;

        return result;
    }
}
