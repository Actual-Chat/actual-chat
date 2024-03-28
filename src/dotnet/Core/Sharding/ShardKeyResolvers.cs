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
    private static readonly MethodInfo NewNullableMethod = typeof(ShardKeyResolvers)
        .GetMethod(nameof(NewNullable), BindingFlags.Static | BindingFlags.Public)!;
    private static readonly MethodInfo NewNotFoundMethod = typeof(ShardKeyResolvers)
        .GetMethod(nameof(NewNotFound), BindingFlags.Static | BindingFlags.Public)!;

    public static ShardKeyResolver<T> NewHashBased<T>() => static x => x?.GetHashCode() ?? ForNull();
    public static ShardKeyResolver<T?> NewNullable<T>(ShardKeyResolver<T> nonNullableResolver)
        where T : struct
        => source => source is { } v
            ? nonNullableResolver.Invoke(v)
            : ForNull();
    public static ShardKeyResolver<T> NewNotFound<T>() => NewHashBased<T>();

    // These properties can be set!
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ForNull() => 0;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ForRandom() => Random.Shared.Next();
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ForString(string? x) => x?.GetDjb2HashCode() ?? 0;

    static ShardKeyResolvers()
    {
        // Value types
        Register<Unit>(static _ => 0);
        Register<Symbol>(static x => ForString(x.Value));
        Register<ChatId>(static x => ForString(x.Value));
        Register<PeerChatId>(static x => ForString(x.Value));
        Register<PlaceId>(static x => ForString(x.Value));
        Register<PlaceChatId>(static x => ForString(x.Value));
        Register<AuthorId>(static x => ForString(x.ChatId.Value));
        Register<ChatEntryId>(static x => ForString(x.ChatId.Value));
        Register<TextEntryId>(static x => ForString(x.ChatId.Value));
        Register<RoleId>(static x => ForString(x.ChatId.Value));
        Register<MentionId>(static x => ForString(x.AuthorId.ChatId.Value));
        Register<UserId>(static x => ForString(x.Value));
        Register<PrincipalId>(static x => ForString(
            x.IsAuthor(out var authorId)
                ? authorId.ChatId.Value
                : x.IsUser(out var userId) ? userId.Value : x.Value));
        Register<ContactId>(static x => ForString(x.OwnerId.Value));
        Register<NotificationId>(static x => ForString(x.UserId.Value));
        Register<MediaId>(static x => ForString(x.Value));

        // Classes
        Register<string>(ForString); // Todo: likely, we should get rid of this kind of shard key
        Register<Session>(x => ForString(x.Id.Value));
        Register<ISessionCommand>(x => ForString(x.Session.Id.Value));
    }

    private static void Register<T>(ShardKeyResolver<T> resolver)
    {
        if (!Registered.TryAdd(typeof(T), resolver))
            throw StandardError.Internal($"ShardKeyResolver for type {typeof(T).GetName()} is already registered.");
    }

    public static int ResolveUntyped(object? source, Requester requester)
        => ReferenceEquals(source, null)
            ? ForNull()
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
                        if (Registered.TryGetValue(baseType, out result))
                            return (Delegate)NewNullableMethod
                                .MakeGenericMethod(baseType)
                                .Invoke(null, [result])!;
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

        return (Delegate)NewNotFoundMethod
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
            ? ForNull()
            : resolver.Invoke(x.ShardKey);
    }
}
