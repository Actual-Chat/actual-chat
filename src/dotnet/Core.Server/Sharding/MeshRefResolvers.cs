namespace ActualChat;

public delegate MeshRef MeshRefResolver<in T>(T source);

public static class MeshRefResolverExt
{
    public static MeshRefResolver<object>? ToUntyped<T>(this MeshRefResolver<T>? resolver)
        => resolver == null ? null : source => resolver.Invoke((T)source);
}

public static class MeshRefResolvers
{
    private static readonly ConcurrentDictionary<Type, Delegate> Registered = new();
    private static readonly ConcurrentDictionary<Type, Delegate?> Resolved = new();
    private static readonly ConcurrentDictionary<Type, MeshRefResolver<object?>?> ResolvedUntyped = new();
    private static readonly MethodInfo GetUntypedInternalMethod = typeof(MeshRefResolvers)
        .GetMethod(nameof(GetUntypedInternal), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CreateShardKeyBasedResolverMethod = typeof(MeshRefResolvers)
        .GetMethod(nameof(CreateShardKeyBasedResolver), BindingFlags.Static | BindingFlags.NonPublic)!;

    public static MeshRefResolver<T> NotFound<T>()
        => _ => throw NotFoundError(typeof(T));

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

    public static MeshRef Resolve(object? source)
    {
        if (source == null)
            return NullResolver.Invoke(default);

        var type = source.GetType();
        var resolver = GetUntyped(type) ?? throw NotFoundError(type);
        return resolver.Invoke(source);
    }

    public static MeshRef Resolve<T>(T source)
    {
        if (source == null)
            return NullResolver.Invoke(default);

        var type = typeof(T);
        var resolver = Get(type) as MeshRefResolver<T> ?? throw NotFoundError(type);
        return resolver.Invoke(source);
    }

    public static MeshRefResolver<object?>? GetUntyped<T>()
        => GetUntyped(typeof(T));
    public static MeshRefResolver<object?>? GetUntyped(Type type)
        => ResolvedUntyped.GetOrAdd(type,
            static t => (MeshRefResolver<object?>?)GetUntypedInternalMethod
                .MakeGenericMethod(t)
                .Invoke(null, Array.Empty<object>()));

    public static MeshRefResolver<T>? Get<T>()
        => Get(typeof(T)) as MeshRefResolver<T>;
    public static Delegate? Get(Type type)
        => Resolved.GetOrAdd(type, static t => {
            if (Registered.TryGetValue(t, out var result))
                return result;

            if (!t.IsValueType)
                foreach (var baseType in t.GetAllBaseTypes(false, true)) {
                    if (Registered.TryGetValue(baseType, out result))
                        return result;
                }

            return (Delegate?)CreateShardKeyBasedResolverMethod
                .MakeGenericMethod(t)
                .Invoke(null, Array.Empty<object>());
        });

    // Private methods

    private static MeshRefResolver<object>? GetUntypedInternal<T>()
        => Get<T>().ToUntyped();

    private static MeshRefResolver<T>? CreateShardKeyBasedResolver<T>()
    {
        var shardKeyResolver = ShardKeyResolvers.Get<T>();
        return shardKeyResolver == null ? null
            : x => MeshRef.Shard(shardKeyResolver.Invoke(x));
    }

    private static Exception NotFoundError(Type type)
        => throw StandardError.Internal($"Can't find MeshRefResolver for type {type.GetName()}.");
}
