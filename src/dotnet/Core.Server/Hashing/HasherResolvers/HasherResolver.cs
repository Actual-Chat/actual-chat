using ActualChat.Hashing.Internal;

namespace ActualChat.Hashing;

public abstract class HasherResolver
{
    public static FuncHasherResolver New(Func<Type, Delegate?> resolver)
        => new (resolver);
    public static FuncHasherResolver New<T>(Hasher<T> hasher, bool isExactMatch = true)
        => isExactMatch
            ? new (type => type == typeof(T) ? (Delegate?)hasher : null)
            : new (type => typeof(T).IsAssignableFrom(type) ? (Delegate?)hasher : null);

    public abstract Delegate? Get(Type type);

    public Hasher<T>? Get<T>()
        => (Hasher<T>?)Get(typeof(T));

    public Hasher<T> GetOrDefault<T>()
        => Get<T>() ?? Hashers.Default<T>();

    // Helpers

    public ExpandingHasherResolver Expanding(Func<ExpandingHasherResolver, Type, Delegate?>? expander = null)
        => new (this, expander);
    public CachingHasherResolver Caching()
        => new (this);

    public HasherResolver Or(Func<Type, Delegate?> resolver)
        => this | New(resolver);
    public HasherResolver Or<T>(Hasher<T> hasher, bool isExactMatch = true)
        => this | New(hasher, isExactMatch);
    public HasherResolver OrDefault()
        => this | DefaultHasherResolver.Instance;

    // Operators

    public static HasherResolver operator |(HasherResolver primary, HasherResolver secondary)
        => new ChainHasherResolver(primary, secondary);
}
