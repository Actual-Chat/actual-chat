using Microsoft.Toolkit.HighPerformance;

namespace ActualChat;

public delegate MeshRef ValueMeshRefResolver<in T>(T source, ShardScheme shardScheme);

public static class ValueMeshRefResolverExt
{
    public static ValueMeshRefResolver<object>? ToUntyped<T>(this ValueMeshRefResolver<T>? resolver)
        => resolver == null ? null : (source, shardScheme) => resolver.Invoke((T)source, shardScheme);
}

public static class ValueMeshRefResolvers
{
    private static readonly ConcurrentDictionary<Type, Delegate> Registered = new();
    private static readonly ConcurrentDictionary<Type, Delegate?> Resolved = new();
    private static readonly ConcurrentDictionary<Type, ValueMeshRefResolver<object?>?> ResolvedUntyped = new();
    private static readonly MethodInfo GetUntypedInternalMethod = typeof(ValueMeshRefResolvers)
        .GetMethod(nameof(GetUntypedInternal), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CreateHasShardKeySourceResolverMethod = typeof(ValueMeshRefResolvers)
        .GetMethod(nameof(CreateHasShardKeySourceResolver), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo NullableMethod = typeof(ValueMeshRefResolvers)
        .GetMethod(nameof(Nullable), BindingFlags.Static | BindingFlags.Public)!;

    public static ValueMeshRefResolver<T> Shard0<T>()
        => static (_, shardScheme) => MeshRef.Shard(shardScheme, 0);
    public static ValueMeshRefResolver<T> Shard<T>(int shardKey)
        => (_, shardScheme) => MeshRef.Shard(shardScheme, shardKey);
    public static ValueMeshRefResolver<T> RandomShard<T>()
        => static (_, shardScheme) => MeshRef.Shard(shardScheme, Random.Shared.Next(shardScheme.ShardCount));
    public static ValueMeshRefResolver<T?> Nullable<T>(ValueMeshRefResolver<T> nonNullableResolver)
        where T : struct
        => (source, shardScheme) => source is { } v
            ? nonNullableResolver.Invoke(v, shardScheme)
            : NullResolver.Invoke(default, shardScheme);
    public static ValueMeshRefResolver<T> NotFound<T>()
        => (_, _) => throw NotFoundError(typeof(T));

    // This property can be set!
    public static ValueMeshRefResolver<Unit> NullResolver { get; set; } = Shard0<Unit>();

    static ValueMeshRefResolvers()
    {
        Register(RandomShard<Unit>());
        Register<MeshNodeId>((source, _) => MeshRef.Node(source));
        Register<int>((source, shardScheme) => MeshRef.Shard(shardScheme, source));
        Register<long>((source, shardScheme) => MeshRef.Shard(shardScheme, source.GetHashCode()));
        Register<string>(ShardFor);
        Register<Symbol>((source, shardScheme) => ShardFor(source.Value, shardScheme));
        Register<ChatId>((source, shardScheme) => ShardFor(source.Value, shardScheme));
        Register<PeerChatId>((source, shardScheme) => ShardFor(source.Value, shardScheme));
        Register<PlaceId>((source, shardScheme) => ShardFor(source.Value, shardScheme));
        Register<PlaceChatId>((source, shardScheme) => ShardFor(source.Value, shardScheme));
        Register<AuthorId>((source, shardScheme) => ShardFor(source.ChatId.Value, shardScheme));
        Register<ChatEntryId>((source, shardScheme) => ShardFor(source.ChatId.Value, shardScheme));
        Register<TextEntryId>((source, shardScheme) => ShardFor(source.ChatId.Value, shardScheme));
        Register<RoleId>((source, shardScheme) => ShardFor(source.ChatId.Value, shardScheme));
        Register<MentionId>((source, shardScheme) => ShardFor(source.AuthorId.ChatId.Value, shardScheme));
        Register<UserId>((source, shardScheme) => ShardFor(source.Value, shardScheme));
        Register<PrincipalId>((source, shardScheme) => ShardFor(
            source.IsAuthor(out var authorId)
                ? authorId.ChatId.Value
                : source.IsUser(out var userId)
                    ? userId.Value
                    : source.Value,
            shardScheme));
        Register<ContactId>((source, shardScheme) => ShardFor(source.OwnerId.Value, shardScheme));
        Register<NotificationId>((source, shardScheme) => ShardFor(source.UserId.Value, shardScheme));
        Register<MediaId>((source, shardScheme) => ShardFor(source.Value, shardScheme));
        return;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static MeshRef ShardFor(string? source, ShardScheme shardScheme)
            => MeshRef.Shard(shardScheme, source?.GetDjb2HashCode() ?? 0);
    }

    public static void Register<T>(ValueMeshRefResolver<T> valueMeshRefResolver)
        => Registered.TryAdd(typeof(T), valueMeshRefResolver);

    public static void Unregister<T>()
        => Registered.TryRemove(typeof(T), out _);

    public static MeshRef Resolve(object? source, ShardScheme shardScheme)
    {
        if (source == null)
            return NullResolver.Invoke(default, shardScheme);

        var type = source.GetType();
        var shardMapper = GetUntyped(type);
        return shardMapper?.Invoke(source, shardScheme) ?? throw NotFoundError(type);
    }

    public static MeshRef Resolve<T>(T source, ShardScheme shardScheme)
    {
        if (source == null)
            return NullResolver.Invoke(default, shardScheme);

        var type = typeof(T);
        var shardMapper = Get(type) as ValueMeshRefResolver<T>;
        return shardMapper?.Invoke(source, shardScheme) ?? throw NotFoundError(type);
    }

    public static ValueMeshRefResolver<object?>? GetUntyped<T>()
        => GetUntyped(typeof(T));
    public static ValueMeshRefResolver<object?>? GetUntyped(Type type)
        => ResolvedUntyped.GetOrAdd(type,
            static t => (ValueMeshRefResolver<object?>?)GetUntypedInternalMethod
                .MakeGenericMethod(t)
                .Invoke(null, Array.Empty<object>()));

    public static ValueMeshRefResolver<T>? Get<T>()
        => Get(typeof(T)) as ValueMeshRefResolver<T>;
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

    private static ValueMeshRefResolver<object>? GetUntypedInternal<T>()
        => Get<T>().ToUntyped();

    private static ValueMeshRefResolver<T?>? CreateHasShardKeySourceResolver<T, TShardKeySource>()
        where T : class, IHasShardKeySource<TShardKeySource>
    {
        var resolver = Get<TShardKeySource>();
        if (resolver == null)
            return null;

        return (value, shardScheme) => value == null
            ? NullResolver.Invoke(default, shardScheme)
            : resolver.Invoke(value.GetShardKeySource(), shardScheme);
    }

    private static Exception NotFoundError(Type type)
        => throw StandardError.Internal($"Can't find ValueMeshRefResolver for type {type.GetName()}.");
}
