using System.Diagnostics.CodeAnalysis;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat;

public delegate int ShardKeyResolver<in T>(T source);

public static class ShardKeyResolverExt
{
    public static ShardKeyResolver<object>? ToUntyped<T>(this ShardKeyResolver<T>? resolver)
        => resolver == null ? null : source => resolver.Invoke((T)source);
}

public static class ShardKeyResolvers
{
    private static readonly ConcurrentDictionary<Type, Unit> KnownInvalidKeyTypes = new();
    private static readonly ConcurrentDictionary<Type, Delegate> Registered = new();
    private static readonly ConcurrentDictionary<Type, Delegate?> Resolved = new();
    private static readonly ConcurrentDictionary<Type, ShardKeyResolver<object?>?> ResolvedUntyped = new();
    private static readonly MethodInfo GetUntypedInternalMethod = typeof(ShardKeyResolvers)
        .GetMethod(nameof(GetUntypedInternal), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CreateHasShardKeyResolverMethod = typeof(ShardKeyResolvers)
        .GetMethod(nameof(CreateHasShardKeyResolver), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo NullableMethod = typeof(ShardKeyResolvers)
        .GetMethod(nameof(Nullable), BindingFlags.Static | BindingFlags.Public)!;

    public static ShardKeyResolver<T> Random<T>() => static _ => System.Random.Shared.Next();
    public static ShardKeyResolver<T> HashCode<T>() => static x => x?.GetHashCode() ?? 0;
    public static ShardKeyResolver<T?> Nullable<T>(ShardKeyResolver<T> nonNullableResolver)
        where T : struct
        => source => source is { } v
            ? nonNullableResolver.Invoke(v)
            : NullResolver.Invoke(default);
    public static ShardKeyResolver<T> NotFound<T>()
        => _ => throw NotFoundError(typeof(T));
    public static ShardKeyResolver<T> Invalid<T>(ShardKeyResolver<T> resolver) => x => {
        if (KnownInvalidKeyTypes.TryAdd(typeof(T), default))
            DefaultLog.LogError("Invalid shard key type: {KeyType}", typeof(T).GetName());
        return resolver.Invoke(x);
    };

    // These properties can be set!
    public static ShardKeyResolver<Unit> NullResolver { get; set; } = static _ => 0;
    public static ShardKeyResolver<string?> StringResolver { get; set; } = static x => x?.GetDjb2HashCode() ?? 0;
    public static ShardKeyResolver<object?> DefaultResolver { get; set; } = static x => x?.GetHashCode() ?? 0;

    static ShardKeyResolvers()
    {
        Register(Random<Unit>());
        Register(Invalid(Random<CancellationToken>()));
        Register(Invalid(HashCode<Moment>()));
        Register<int>(static x => x);
        Register<long>(static x => x.GetHashCode());
        Register<string>(StringResolver);
        Register<Symbol>(x => StringResolver(x.Value));
        Register<ChatId>(x => StringResolver(x.Value));
        Register<PeerChatId>(x => StringResolver(x.Value));
        Register<PlaceId>(x => StringResolver(x.Value));
        Register<PlaceChatId>(x => StringResolver(x.Value));
        Register<AuthorId>(x => StringResolver(x.ChatId.Value));
        Register<ChatEntryId>(x => StringResolver(x.ChatId.Value));
        Register<TextEntryId>(x => StringResolver(x.ChatId.Value));
        Register<RoleId>(x => StringResolver(x.ChatId.Value));
        Register<MentionId>(x => StringResolver(x.AuthorId.ChatId.Value));
        Register<UserId>(x => StringResolver(x.Value));
        Register<PrincipalId>(x => StringResolver(
            x.IsAuthor(out var authorId)
                ? authorId.ChatId.Value
                : x.IsUser(out var userId) ? userId.Value : x.Value));
        Register<ContactId>(x => StringResolver(x.OwnerId.Value));
        Register<NotificationId>(x => StringResolver(x.UserId.Value));
        Register<MediaId>(x => StringResolver(x.Value));
    }

    public static void Register<T>(ShardKeyResolver<T> resolver)
    {
        if (!Registered.TryAdd(typeof(T), resolver))
            throw StandardError.Internal($"ShardKeyResolver for type {typeof(T).GetName()} is already registered.");
    }

    public static int Resolve(object? source)
    {
        if (source == null)
            return NullResolver.Invoke(default);

        var type = source.GetType();
        var resolver = GetUntyped(type) ?? throw NotFoundError(type);
        return resolver.Invoke(source);
    }

    public static int Resolve<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T source)
    {
        if (source == null)
            return NullResolver.Invoke(default);

        var type = typeof(T);
        var resolver = Get(type) as ShardKeyResolver<T> ?? throw NotFoundError(type);
        return resolver.Invoke(source);
    }

    public static ShardKeyResolver<object?>? GetUntyped<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        => GetUntyped(typeof(T));
    public static ShardKeyResolver<object?>? GetUntyped(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => ResolvedUntyped.GetOrAdd(type,
            static t => (ShardKeyResolver<object?>?)GetUntypedInternalMethod
                .MakeGenericMethod(t)
                .Invoke(null, Array.Empty<object>()));

    public static ShardKeyResolver<T>? Get<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        => Get(typeof(T)) as ShardKeyResolver<T>;
    public static Delegate? Get(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => Resolved.GetOrAdd(type, static ([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] t) => {
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
                if (baseType is { IsInterface: true, IsGenericType: true } && baseType.GetGenericTypeDefinition() == typeof(IHasShardKey<>)) {
                    var shardKeyType = baseType.GetGenericArguments()[0];
                    return (Delegate?)CreateHasShardKeyResolverMethod
                        .MakeGenericMethod(t, shardKeyType)
                        .Invoke(null, Array.Empty<object>());
                }
            }

            return null;
        });

    // Private methods

    private static ShardKeyResolver<object>? GetUntypedInternal<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        => Get<T>().ToUntyped();

    private static ShardKeyResolver<T>? CreateHasShardKeyResolver<
        T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TShardKey>()
        where T : IHasShardKey<TShardKey>
    {
        var resolver = Get<TShardKey>();
        if (resolver == null)
            return null;

        if (typeof(T).IsValueType)
            return x => resolver.Invoke(x.ShardKey);

        return x => ReferenceEquals(x, null)
            ? NullResolver.Invoke(default)
            : resolver.Invoke(x.ShardKey);
    }

    private static Exception NotFoundError(Type type)
        => throw StandardError.Internal($"Can't find ValueMeshRefResolver for type {type.GetName()}.");
}
