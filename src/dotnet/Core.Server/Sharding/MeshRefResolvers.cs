namespace ActualChat;

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

    public static MeshRefResolver<Unit> NullResolver { get; set; } = static _ => MeshRef.Shard(0);

    static MeshRefResolvers()
    {
        Register<NodeRef>(MeshRef.Node);
        Register<NodeRef?>(x => x is { } nodeRef ? MeshRef.Node(nodeRef) : MeshRef.None);
        Register<StreamId>(x => MeshRef.Node(x.NodeRef));
        Register<StreamId?>(x => x is { } v ? MeshRef.Node(v.NodeRef) : MeshRef.None);
        Register<IHasNodeRef?>(x => x != null ? MeshRef.Node(x.NodeRef) : MeshRef.None);
    }

    public static void Register<T>(MeshRefResolver<T> resolver)
    {
        if (!Registered.TryAdd(typeof(T), resolver))
            throw StandardError.Internal($"MeshRefResolver for type {typeof(T).GetName()} is already registered.");
    }

    public static MeshRef ResolveUntyped(object? source, Requester requester)
        => ReferenceEquals(source, null)
            ? NullResolver.Invoke(default)
            : GetUntyped(source.GetType(), requester).Invoke(source);

    public static MeshRefResolver<object?> GetUntyped<T>(Requester requester)
        => GetUntyped(typeof(T), requester);
    public static MeshRefResolver<object?> GetUntyped(Type type, Requester requester)
        => ResolvedUntyped.GetOrAdd(type,
            static (type1, requester1) => (MeshRefResolver<object?>?)GetUntypedInternalMethod
                .MakeGenericMethod(type1)
                .Invoke(null, [requester1])!,
            requester);

    public static MeshRefResolver<T> Get<T>(Requester requester)
        => (MeshRefResolver<T>)Get(typeof(T), requester);
    public static Delegate Get(Type type, Requester requester)
        => Resolved.GetOrAdd(type, static (type1, requester1) => {
            if (Registered.TryGetValue(type1, out var result))
                return result;

            if (!type1.IsValueType)
                foreach (var baseType in type1.GetAllBaseTypes(false, true)) {
                    if (Registered.TryGetValue(baseType, out result))
                        return result;
                }

            return (Delegate)CreateShardKeyBasedResolverMethod
                .MakeGenericMethod(type1)
                .Invoke(null, [requester1])!;
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
