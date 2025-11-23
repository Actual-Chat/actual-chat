namespace ActualChat.Sharding;

public delegate MeshRef MeshRefResolver<in T>(T source);

public static class MeshRefResolverExt
{
    public static MeshRefResolver<object> ToUntyped<T>(this MeshRefResolver<T> resolver)
        => source => resolver.Invoke((T)source);
}

public static class MeshRefResolvers
{
    private static readonly ConcurrentDictionary<Type, Delegate> Registered = new();
    private static readonly ConcurrentDictionary<Type, Delegate> Resolved = new();
    private static readonly ConcurrentDictionary<Type, MeshRefResolver<object?>> ResolvedUntyped = new();
    private static readonly MethodInfo GetUntypedInternalMethod = typeof(MeshRefResolvers)
        .GetMethod(nameof(GetUntypedInternal), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CreateShardKeyBasedResolverMethod = typeof(MeshRefResolvers)
        .GetMethod(nameof(CreateShardKeyBasedResolver), BindingFlags.Static | BindingFlags.NonPublic)!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MeshRef RandomShard() => MeshRef.Shard(ShardKeyResolvers.RandomShard());
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MeshRef ForString(string? x) => MeshRef.Shard(x?.GetXxHash3() ?? 0);

    static MeshRefResolvers()
    {
        // NOTE(AY):Returning MeshRef.None from MeshRefResolver means
        // MeshRpcPeerRef.Get(MeshRef meshRef) will fail with an exception,
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

    public static MeshRefResolver<object?> GetUntyped(Type type, Requester requester)
        => ResolvedUntyped.GetOrAdd(type,
            static (type1, requester1) => (MeshRefResolver<object?>?)GetUntypedInternalMethod
                .MakeGenericMethod(type1)
                .Invoke(null, [requester1])!,
            requester);

    public static MeshRefResolver<T> Get<T>(Requester requester)
        => (MeshRefResolver<T>)Get(typeof(T), requester);
    public static Delegate Get(Type type, Requester requester)
        => Resolved.GetOrAdd(type, static (type, requester) => {
            if (Registered.TryGetValue(type, out var result))
                return result;

            if (!type.IsValueType)
                foreach (var baseType in type.GetAllBaseTypes(false, true)) {
                    if (Registered.TryGetValue(baseType, out result))
                        return result;
                }

            return (Delegate)CreateShardKeyBasedResolverMethod
                .MakeGenericMethod(type)
                .Invoke(null, [requester])!;
        }, requester);

    // Private methods

    private static MeshRefResolver<object> GetUntypedInternal<T>(Requester requester)
        => Get<T>(requester).ToUntyped();

    private static MeshRefResolver<T> CreateShardKeyBasedResolver<T>(Requester requester)
    {
        var shardKeyResolver = ShardKeyResolvers.Get<T>(requester);
        return x => MeshRef.Shard(shardKeyResolver.Invoke(x));
    }
}
