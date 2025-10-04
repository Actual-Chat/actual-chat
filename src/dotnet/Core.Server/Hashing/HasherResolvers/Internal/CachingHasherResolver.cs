namespace ActualChat.Hashing.Internal;

public sealed class CachingHasherResolver : HasherResolver
{
    private readonly ConcurrentDictionary<Type, Delegate?> _cache = new();
    private readonly Func<Type, Delegate?> _builder;

    public HasherResolver BaseResolver { get; }

    public CachingHasherResolver(HasherResolver baseResolver)
    {
        BaseResolver = baseResolver;
        _builder = type => BaseResolver.Get(type);
    }

    public override Delegate? Get(Type type)
        => _cache.GetOrAdd(type, _builder);
}
