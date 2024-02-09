using Microsoft.Toolkit.HighPerformance;

namespace ActualChat;

public delegate MeshRef MeshRefResolver<in T>(T source);

public static class ValueMeshRefResolverExt
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
    private static readonly MethodInfo CreateHasShardKeySourceResolverMethod = typeof(MeshRefResolvers)
        .GetMethod(nameof(CreateHasShardKeySourceResolver), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo NullableMethod = typeof(MeshRefResolvers)
        .GetMethod(nameof(Nullable), BindingFlags.Static | BindingFlags.Public)!;

    public static MeshRefResolver<T> Shard0<T>() => static _ => MeshRef.Shard(0);
    public static MeshRefResolver<T> Shard<T>(int key) => _ => MeshRef.Shard(key);
    public static MeshRefResolver<T> RandomShard<T>()
        => static _ => MeshRef.Shard(Random.Shared.Next());
    public static MeshRefResolver<T?> Nullable<T>(MeshRefResolver<T> nonNullableResolver)
        where T : struct
        => source => source is { } v
            ? nonNullableResolver.Invoke(v)
            : NullResolver.Invoke(default);
    public static MeshRefResolver<T> NotFound<T>()
        => _ => throw NotFoundError(typeof(T));

    // This property can be set!
    public static MeshRefResolver<Unit> NullResolver { get; set; } = Shard0<Unit>();

    static MeshRefResolvers()
    {
        Register(RandomShard<Unit>());
        Register<NodeRef>(MeshRef.Node);
        Register<int>(MeshRef.Shard);
        Register<long>(source => MeshRef.Shard(source.GetHashCode()));
        Register<string>(ShardFor);
        Register<Symbol>(source => ShardFor(source.Value));
        Register<ChatId>(source => ShardFor(source.Value));
        Register<PeerChatId>(source => ShardFor(source.Value));
        Register<PlaceId>(source => ShardFor(source.Value));
        Register<PlaceChatId>(source => ShardFor(source.Value));
        Register<AuthorId>(source => ShardFor(source.ChatId.Value));
        Register<ChatEntryId>(source => ShardFor(source.ChatId.Value));
        Register<TextEntryId>(source => ShardFor(source.ChatId.Value));
        Register<RoleId>(source => ShardFor(source.ChatId.Value));
        Register<MentionId>(source => ShardFor(source.AuthorId.ChatId.Value));
        Register<UserId>(source => ShardFor(source.Value));
        Register<PrincipalId>(source => ShardFor(
            source.IsAuthor(out var authorId)
                ? authorId.ChatId.Value
                : source.IsUser(out var userId) ? userId.Value : source.Value));
        Register<ContactId>(source => ShardFor(source.OwnerId.Value));
        Register<NotificationId>(source => ShardFor(source.UserId.Value));
        Register<MediaId>(source => ShardFor(source.Value));
        return;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static MeshRef ShardFor(string? source)
            => MeshRef.Shard(source?.GetDjb2HashCode() ?? 0);
    }

    public static void Register<T>(MeshRefResolver<T> meshRefResolver)
        => Registered.TryAdd(typeof(T), meshRefResolver);

    public static void Unregister<T>()
        => Registered.TryRemove(typeof(T), out _);

    public static MeshRef Resolve(object? source)
    {
        if (source == null)
            return NullResolver.Invoke(default);

        var type = source.GetType();
        var shardMapper = GetUntyped(type);
        return shardMapper?.Invoke(source) ?? throw NotFoundError(type);
    }

    public static MeshRef Resolve<T>(T source)
    {
        if (source == null)
            return NullResolver.Invoke(default);

        var type = typeof(T);
        var shardMapper = Get(type) as MeshRefResolver<T>;
        return shardMapper?.Invoke(source) ?? throw NotFoundError(type);
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

            if (t.IsValueType) {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                    var baseType = t.GetGenericArguments()[0];
                    var nonNullableResolver = Registered.GetValueOrDefault(baseType);
                    if (nonNullableResolver != null)
                        return (Delegate?)NullableMethod
                            .MakeGenericMethod(baseType)
                            .Invoke(null, [ nonNullableResolver ]);
                }
                return null;
            }

            foreach (var baseType in t.GetAllBaseTypes(false, true)) {
                if (Registered.TryGetValue(baseType, out result))
                    return result;
                if (baseType is { IsInterface: true, IsGenericType: true } && baseType.GetGenericTypeDefinition() == typeof(IHasShardKeySource<>)) {
                    var shardKeyType = baseType.GetGenericArguments()[0];
                    return (Delegate?)CreateHasShardKeySourceResolverMethod
                        .MakeGenericMethod(t, shardKeyType)
                        .Invoke(null, Array.Empty<object>());
                }
            }

            return null;
        });

    // Private methods

    private static MeshRefResolver<object>? GetUntypedInternal<T>()
        => Get<T>().ToUntyped();

    private static MeshRefResolver<T?>? CreateHasShardKeySourceResolver<T, TShardKey>()
        where T : class, IHasShardKeySource<TShardKey>
    {
        var resolver = Get<TShardKey>();
        if (resolver == null)
            return null;

        return value => value == null
            ? NullResolver.Invoke(default)
            : resolver.Invoke(value.GetShardKeySource());
    }

    private static Exception NotFoundError(Type type)
        => throw StandardError.Internal($"Can't find ValueMeshRefResolver for type {type.GetName()}.");
}
