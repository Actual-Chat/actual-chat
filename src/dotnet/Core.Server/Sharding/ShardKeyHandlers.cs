using Microsoft.Toolkit.HighPerformance;

namespace ActualChat;

public delegate int ShardKeyHandler<in T>(T shardKey, Sharding sharding);

public static class ShardKeyHandlerExt
{
    public static ShardKeyHandler<object>? ToUntyped<T>(this ShardKeyHandler<T>? shardMapper)
        => shardMapper == null ? null : (shardKey, sharding) => shardMapper.Invoke((T)shardKey, sharding);
}

public static class ShardKeyHandlers
{
    private static readonly ConcurrentDictionary<Type, Delegate> RegisteredMappers = new();
    private static readonly ConcurrentDictionary<Type, Delegate?> ResolvedMappers = new();
    private static readonly ConcurrentDictionary<Type, ShardKeyHandler<object?>?> ResolvedUntypedMappers = new();
    private static readonly MethodInfo GetUntypedInternalMethod = typeof(ShardKeyHandlers)
        .GetMethod(nameof(GetUntypedInternal), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CreateHasShardKeyHandlerMethod = typeof(ShardKeyHandlers)
        .GetMethod(nameof(CreateHasShardKeyHandlerMethod), BindingFlags.Static | BindingFlags.NonPublic)!;

    public static ShardKeyHandler<T> Always0<T>() => static (_, _) => 0;
    public static ShardKeyHandler<T> Always<T>(int result) => (_, _) => result;
    public static ShardKeyHandler<T> Random<T>()
        => static (_, sharding) => System.Random.Shared.Next(sharding.ShardCount);
    public static ShardKeyHandler<T?> Nullable<T>(ShardKeyHandler<T> keyHandler)
        where T : struct
        => (shardKey, sharding) => shardKey is { } v ? keyHandler.Invoke(v, sharding) : 0;
    public static ShardKeyHandler<T> NotFound<T>()
        => (_, _) => throw StandardError.Internal($"Can't find shard mapper for type {typeof(T).GetName()}.");
    public static ShardKeyHandler<Unit> NullKeyHandler { get; set; }
        = Always0<Unit>();

    static ShardKeyHandlers()
    {
        Register(Random<Unit>());
        Register<int>((shardKey, _) => shardKey);
        Register<long>((shardKey, _) => shardKey.GetHashCode());
        Register<string>((shardKey, _) => Hash(shardKey));
        Register<Symbol>((shardKey, _) => Hash(shardKey.Value));
        Register<ChatId>((shardKey, _) => Hash(shardKey.Value));
        Register<PeerChatId>((shardKey, _) => Hash(shardKey.Value));
        Register<PlaceId>((shardKey, _) => Hash(shardKey.Value));
        Register<PlaceChatId>((shardKey, _) => Hash(shardKey.Value));
        Register<AuthorId>((shardKey, _) => Hash(shardKey.ChatId.Value));
        Register<ChatEntryId>((shardKey, _) => Hash(shardKey.ChatId.Value));
        Register<TextEntryId>((shardKey, _) => Hash(shardKey.ChatId.Value));
        Register<RoleId>((shardKey, _) => Hash(shardKey.ChatId.Value));
        Register<MentionId>((shardKey, _) => Hash(shardKey.AuthorId.ChatId.Value));
        Register<UserId>((shardKey, _) => Hash(shardKey.Value));
        Register<PrincipalId>((shardKey, _)
            => Hash(shardKey.IsAuthor(out var authorId)
                ? authorId.ChatId.Value
                : shardKey.IsUser(out var userId)
                    ? userId.Value
                    : shardKey.Value));
        Register<ContactId>((shardKey, _) => Hash(shardKey.OwnerId.Value));
        Register<NotificationId>((shardKey, _) => Hash(shardKey.UserId.Value));
        Register<MediaId>((shardKey, _) => Hash(shardKey.Value));
        return;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Hash(string? shardKey) => shardKey?.GetDjb2HashCode() ?? 0;
    }

    public static void Register<T>(ShardKeyHandler<T> shardKeyHandler)
        => RegisteredMappers.TryAdd(typeof(T), shardKeyHandler);

    public static void Unregister<T>()
        => RegisteredMappers.TryRemove(typeof(T), out _);

    public static int Map(object? shardKey, Sharding sharding)
    {
        if (shardKey == null)
            return NullKeyHandler.Invoke(default, sharding);

        var type = shardKey.GetType();
        var shardMapper = GetUntyped(type);
        return shardMapper?.Invoke(shardKey, sharding) ?? throw NotFoundError(type);
    }

    public static int Map<T>(T shardKey, Sharding sharding)
    {
        if (shardKey == null)
            return NullKeyHandler.Invoke(default, sharding);

        var type = typeof(T);
        var shardMapper = Get(type) as ShardKeyHandler<T>;
        return shardMapper?.Invoke(shardKey, sharding) ?? throw NotFoundError(type);
    }

    public static ShardKeyHandler<object?>? GetUntyped<T>()
        => GetUntyped(typeof(T));
    public static ShardKeyHandler<object?>? GetUntyped(Type type)
        => ResolvedUntypedMappers.GetOrAdd(type,
            static t => (ShardKeyHandler<object?>?)GetUntypedInternalMethod
                .MakeGenericMethod(t)
                .Invoke(null, Array.Empty<object>()));

    public static ShardKeyHandler<T>? Get<T>()
        => Get(typeof(T)) as ShardKeyHandler<T>;
    public static Delegate? Get(Type type)
        => ResolvedMappers.GetOrAdd(type, static t => {
            if (RegisteredMappers.TryGetValue(t, out var result))
                return result;

            if (t.IsValueType) {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                    var baseType = t.GetGenericArguments()[0];
                    return RegisteredMappers.GetValueOrDefault(baseType);
                }
                return null;
            }

            foreach (var baseType in t.GetAllBaseTypes(false, true)) {
                if (RegisteredMappers.TryGetValue(baseType, out result))
                    return result;
                if (baseType is { IsInterface: true, IsGenericType: true } && baseType.GetGenericTypeDefinition() == typeof(IHasShardKey<>)) {
                    var shardKeyType = baseType.GetGenericArguments()[0];
                    return (Delegate?)CreateHasShardKeyHandlerMethod
                        .MakeGenericMethod(t, shardKeyType)
                        .Invoke(null, Array.Empty<object>());
                }
            }

            return null;
        });

    public static Exception NotFoundError(Type type)
        => throw StandardError.Internal($"Can't find shard mapper for type {type.GetName()}.");

    // Private methods

    private static ShardKeyHandler<object>? GetUntypedInternal<T>()
        => Get<T>().ToUntyped();

    private static ShardKeyHandler<T?>? CreateHasShardKeyHandler<T, TShardKey>()
        where T : class, IHasShardKey<TShardKey>
    {
        var shardKeyHandler = Get<TShardKey>();
        if (shardKeyHandler == null)
            return null;

        return (value, sharding) => value == null ? 0 : shardKeyHandler.Invoke(value.GetShardKey(), sharding);
    }
}
