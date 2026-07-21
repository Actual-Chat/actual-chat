using ActualLab.Caching;

namespace ActualChat.Sharding;

public delegate MeshRef MeshRefResolver<in T>(T source);

public static class MeshRefResolverExt
{
    public static MeshRefResolver<object?> ToUntyped<T>(this MeshRefResolver<T> resolver)
        => source => resolver.Invoke((T)source!);
}

public static class MeshRefResolvers
{
    private static readonly ConcurrentDictionary<Type, Delegate> Registered = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MeshRef RandomShard() => MeshRef.Shard(ShardKeyResolvers.RandomShard());
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MeshRef ForString(string? x) => MeshRef.Shard(x?.GetXxHash3() ?? 0);

    static MeshRefResolvers()
    {
        // NOTE(AY):Returning MeshRef.None from MeshRefResolver means
        // MeshRpcRef.Get(MeshRef meshRef) will fail with an exception,
        // so the call with such an argument will fail too.
        Register<ThisNodeRef>(_ => MeshRef.ThisNodeAlias);
        Register<IRequiresThisNode>(_ => MeshRef.ThisNodeAlias);
        Register<ZeroShardRef>(_ => MeshRef.ZeroShard);
        Register<IRequiresZeroShard>(_ => MeshRef.ZeroShard);
        Register<RandomShardRef>(_ => RandomShard());
        Register<IRequiresRandomShard>(_ => RandomShard());
        Register<NodeRef>(MeshRef.Node);
        Register<NodeRef?>(x => x ?? MeshRef.None);
        Register<IHasNodeRef?>(x => x != null ? MeshRef.Node(x.NodeRef) : MeshRef.None);
        Register<StreamId?>(x => x is { } v ? MeshRef.Node(v.NodeRef) : MeshRef.None);
    }

    public static void Register<T>(MeshRefResolver<T> resolver)
    {
        if (!Registered.TryAdd(typeof(T), resolver))
            throw StandardError.Internal($"MeshRefResolver for type {typeof(T).GetName()} is already registered.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MeshRefResolver<object?> GetUntyped(Type type)
        => GenericInstanceCache.GetUnsafe<MeshRefResolver<object?>>(typeof(UntypedFactory<>), type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MeshRefResolver<T> Get<T>()
        => GenericInstanceCache.GetUnsafe<MeshRefResolver<T>>(typeof(Factory<>), typeof(T));

    public static Delegate Get(Type type)
        => Unsafe.As<Delegate>(GenericInstanceCache.Get(typeof(Factory<>), type)!);

    // Private methods

    private static MeshRefResolver<T> CreateShardKeyBasedResolver<T>()
    {
        var shardKeyResolver = ShardKeyResolvers.Get<T>();
        return x => MeshRef.Shard(shardKeyResolver.Invoke(x));
    }

    // Nested types

    private sealed class Factory<T> : GenericInstanceFactory, IGenericInstanceFactory<MeshRefResolver<T>>
    {
        public override MeshRefResolver<T> Generate()
        {
            if (Registered.TryGetValue(typeof(T), out var result))
                return (MeshRefResolver<T>)result;

            if (!typeof(T).IsValueType) {
                foreach (var baseType in typeof(T).GetAllBaseTypes(false, true)) {
                    if (Registered.TryGetValue(baseType, out result))
                        return (MeshRefResolver<T>)result;
                }
            }

            return CreateShardKeyBasedResolver<T>();
        }
    }

    private sealed class UntypedFactory<T> : GenericInstanceFactory, IGenericInstanceFactory<MeshRefResolver<object?>>
    {
        public override MeshRefResolver<object?> Generate()
            => Get<T>().ToUntyped();
    }
}
