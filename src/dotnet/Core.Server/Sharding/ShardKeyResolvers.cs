using ActualLab.Caching;

namespace ActualChat.Sharding;

public delegate ShardKey ShardKeyResolver<in T>(T source);

public static class ShardKeyResolverExt
{
    public static ShardKeyResolver<object?> ToUntyped<T>(this ShardKeyResolver<T> resolver)
        => source => resolver.Invoke((T)source!);
}

public static class ShardKeyResolvers
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For(typeof(ShardKeyResolvers));

    private static readonly ConcurrentDictionary<Type, Delegate> Registered = new();

    // Set before the first resolution: resolvers are cached per type.
    // Production retains the warning + hash fallback; other hosts fail on missing stable routing.
    public static bool MustThrowOnNotFound { get; set; }

    public static ShardKeyResolver<T> NewHashBased<T>() => static x => ShardKey.New(x?.GetHashCode() ?? 0);
    public static ShardKeyResolver<T?> NewNullable<T>(ShardKeyResolver<T> nonNullableResolver)
        where T : struct
        => source => source is { } v ? nonNullableResolver.Invoke(v) : default;

    public static ShardKeyResolver<T> NewNotFound<T>()
    {
        if (MustThrowOnNotFound)
            return static _ => throw StandardError.Internal(
                $"ShardKeyResolver not found for type {typeof(T).GetName()}.");

        var hashBased = NewHashBased<T>();
        return x => {
            Log.LogWarning("ShardKeyResolver not found for type {TypeName}", typeof(T).GetName());
            return hashBased(x);
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ShardKey RandomShard() => ShardKey.New(Random.Shared.Next());
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ShardKey ForString(string? x) => ShardKey.New(x);

    static ShardKeyResolvers()
    {
        Register<Unit>(static _ => default);
        Register<int>(static x => ShardKey.New(x));
        Register<uint>(static x => new ShardKey(x));
        Register<Symbol>(static x => ShardKey.New(x.Value));
        Register<UserIdentity>(static x => ShardKey.New(x.Id));
        Register<string>(ShardKey.New);
        Register<Session>(static x => ShardKey.New(x.Id));
        Register<ISessionCommand>(static x => ShardKey.New(x.Session.Id));
    }

    public static void Register<T>(ShardKeyResolver<T> resolver)
    {
        var type = typeof(T);
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        if (typeof(IHasShardKey).IsAssignableFrom(underlyingType))
            throw StandardError.Constraint(
                $"'{type.GetName()}' implements IHasShardKey and must define its own shard key.");

        if (Registered.TryAdd(type, (ShardKeyResolver<T>)NullableResolver))
            return;

        throw StandardError.Internal($"ShardKeyResolver for type {type.GetName()} is already registered.");

        ShardKey NullableResolver(T x)
            => x is not null ? resolver(x) : default;
    }

    public static ShardKeyResolver<object?> GetUntyped(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => GenericInstanceCache.GetUnsafe<ShardKeyResolver<object?>>(typeof(UntypedFactory<>), type);

    public static ShardKeyResolver<T> Get<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        => GenericInstanceCache.GetUnsafe<ShardKeyResolver<T>>(typeof(Factory<>), typeof(T));

    public static Delegate Get(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => Unsafe.As<Delegate>(GenericInstanceCache.Get(typeof(Factory<>), type)!);

    // Nested types

    private sealed class Factory<T> : GenericInstanceFactory, IGenericInstanceFactory<ShardKeyResolver<T>>
    {
        public override ShardKeyResolver<T> Generate()
        {
            var type = typeof(T);
            if (typeof(IHasShardKey).IsAssignableFrom(type))
                return (ShardKeyResolver<T>)GenericInstanceCache.Get(typeof(HasShardKeyFactory<>), type)!;

            if (Registered.TryGetValue(type, out var result))
                return (ShardKeyResolver<T>)result;

            if (Nullable.GetUnderlyingType(type) is { } underlyingType)
                return (ShardKeyResolver<T>)GenericInstanceCache.Get(typeof(NullableFactory<>), underlyingType)!;

            if (!type.IsValueType)
                foreach (var baseType in type.GetAllBaseTypes(false, true))
                    if (Registered.TryGetValue(baseType, out result))
                        return (ShardKeyResolver<T>)result;

            Log.LogError("ShardKeyResolvers: shard key type: {Type}, requester: {Requester}",
                type.GetName(), "GenericInstanceFactory");
            return NewNotFound<T>();
        }
    }

    private sealed class NullableFactory<T> : GenericInstanceFactory, IGenericInstanceFactory<ShardKeyResolver<T?>>
        where T : struct
    {
        public override ShardKeyResolver<T?> Generate()
            => NewNullable(Get<T>());
    }

    private sealed class HasShardKeyFactory<T>
        : GenericInstanceFactory, IGenericInstanceFactory<ShardKeyResolver<T>>
        where T : IHasShardKey
    {
        public override ShardKeyResolver<T> Generate()
            => static x => x is null ? default : x.ShardKey;
    }

    private sealed class UntypedFactory<T>
        : GenericInstanceFactory, IGenericInstanceFactory<ShardKeyResolver<object?>>
    {
        public override ShardKeyResolver<object?> Generate()
            => Get<T>().ToUntyped();
    }
}
