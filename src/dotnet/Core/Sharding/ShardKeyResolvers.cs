using System.Diagnostics.CodeAnalysis;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat;

public delegate int ShardKeyResolver<in T>(T source);

public static class ShardKeyResolverExt
{
    public static ShardKeyResolver<object> ToUntyped<T>(this ShardKeyResolver<T> resolver)
        => source => resolver.Invoke((T)source);
}

public static class ShardKeyResolvers
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= DefaultLogFor(typeof(ShardKeyResolvers));

    private static readonly ConcurrentDictionary<Type, Delegate> Registered = new();
    private static readonly ConcurrentDictionary<Type, Delegate> ResolvedCache = new();
    private static readonly ConcurrentDictionary<Type, ShardKeyResolver<object?>> ResolvedUntyped = new();
    private static readonly MethodInfo GetUntypedInternalMethod = typeof(ShardKeyResolvers)
        .GetMethod(nameof(GetUntypedInternal), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CreateHasShardKeyResolverMethod = typeof(ShardKeyResolvers)
        .GetMethod(nameof(CreateHasShardKeyResolver), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo NullableMethod = typeof(ShardKeyResolvers)
        .GetMethod(nameof(Nullable), BindingFlags.Static | BindingFlags.Public)!;
    private static readonly MethodInfo UnregisteredMethod = typeof(ShardKeyResolvers)
        .GetMethod(nameof(Unregistered), BindingFlags.Static | BindingFlags.Public)!;

    public static ShardKeyResolver<T> Random<T>() => static _ => System.Random.Shared.Next();
    public static ShardKeyResolver<T> HashCode<T>() => static x => x?.GetHashCode() ?? 0;
    public static ShardKeyResolver<T?> Nullable<T>(ShardKeyResolver<T> nonNullableResolver)
        where T : struct
        => source => source is { } v
            ? nonNullableResolver.Invoke(v)
            : NullResolver.Invoke(default);
    public static ShardKeyResolver<T> Unregistered<T>() => HashCode<T>();

    // These properties can be set!
    public static ShardKeyResolver<Unit> NullResolver { get; set; }
        = static _ => 0;
    public static ShardKeyResolver<string?> StringResolver { get; set; }
        = static x => x?.GetDjb2HashCode() ?? 0;
    public static ShardKeyResolver<object?> ObjectResolver { get; set; }
        = static x => x is string s ? StringResolver.Invoke(s) : x?.GetHashCode() ?? 0;

    static ShardKeyResolvers()
    {
        Register(Random<Unit>());
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

    private static void Register<T>(ShardKeyResolver<T> resolver)
    {
        if (!Registered.TryAdd(typeof(T), resolver))
            throw StandardError.Internal($"ShardKeyResolver for type {typeof(T).GetName()} is already registered.");
    }

    public static int ResolveUntyped(object? source, Requester requester)
        => ReferenceEquals(source, null)
            ? NullResolver.Invoke(default)
            : GetUntyped(source.GetType(), requester).Invoke(source);

    public static ShardKeyResolver<object?> GetUntyped(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type,
        Requester requester)
        => ResolvedUntyped.GetOrAdd(type,
            static (type1, requester1) => (ShardKeyResolver<object?>)GetUntypedInternalMethod
                .MakeGenericMethod(type1)
                .Invoke(null, [requester1])!,
            requester);

    public static ShardKeyResolver<T> Get<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Requester requester)
        => (ShardKeyResolver<T>)Get(typeof(T), requester);

    // Private methods

    public static Delegate Get(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type,
        Requester requester)
        => ResolvedCache.GetOrAdd(type,
            static ([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] type1, requester1) => {
                if (Registered.TryGetValue(type1, out var result))
                    return result;

                if (type1.IsValueType) {
                    if (type1.IsGenericType && type1.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                        var baseType = type1.GetGenericArguments()[0];
                        var nonNullableResolver = Registered.GetValueOrDefault(baseType);
                        if (nonNullableResolver != null)
                            return (Delegate)NullableMethod
                                .MakeGenericMethod(baseType)
                                .Invoke(null, [nonNullableResolver])!;
                    }
                    return NotFound(type1, requester1);
                }

                foreach (var baseType in type1.GetAllBaseTypes(false, true)) {
                    if (Registered.TryGetValue(baseType, out result))
                        return result;
                    if (baseType is { IsInterface: true, IsGenericType: true } && baseType.GetGenericTypeDefinition() == typeof(IHasShardKey<>)) {
                        var shardKeyType = baseType.GetGenericArguments()[0];
                        return (Delegate)CreateHasShardKeyResolverMethod
                            .MakeGenericMethod(type1, shardKeyType)
                            .Invoke(null, [requester1])!;
                    }
                }
                return NotFound(type1, requester1);
            }, requester);

    private static Delegate NotFound(Type type, Requester requester)
    {
        Log.LogError("ShardKeyResolvers: shard key type: {Type}, requester: {Requester}",
            type.GetName(), requester.ToString());

        return (Delegate)UnregisteredMethod
            .MakeGenericMethod(type)
            .Invoke(null, Array.Empty<object>())!;
    }

    private static ShardKeyResolver<object> GetUntypedInternal<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Requester requester)
        => Get<T>(requester).ToUntyped();

    private static ShardKeyResolver<T> CreateHasShardKeyResolver<
        T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TShardKey>(Requester requester)
        where T : IHasShardKey<TShardKey>
    {
        var resolver = Get<TShardKey>(requester);
        if (typeof(T).IsValueType)
            return x => resolver.Invoke(x.ShardKey);

        return x => ReferenceEquals(x, null)
            ? NullResolver.Invoke(default)
            : resolver.Invoke(x.ShardKey);
    }
}
