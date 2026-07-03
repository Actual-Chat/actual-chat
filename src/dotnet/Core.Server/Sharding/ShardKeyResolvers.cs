using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualLab.Caching;

namespace ActualChat.Sharding;

public delegate int ShardKeyResolver<in T>(T source);

public static class ShardKeyResolverExt
{
    public static ShardKeyResolver<object?> ToUntyped<T>(this ShardKeyResolver<T> resolver)
        => source => resolver.Invoke((T)source!);
}

public static class ShardKeyResolvers
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For(typeof(ShardKeyResolvers));

    private static readonly ConcurrentDictionary<Type, Delegate> Registered = new();

    public static ShardKeyResolver<T> NewHashBased<T>() => static x => x?.GetHashCode() ?? 0;
    public static ShardKeyResolver<T?> NewNullable<T>(ShardKeyResolver<T> nonNullableResolver)
        where T : struct
        => source => source is { } v
            ? nonNullableResolver.Invoke(v)
            : 0;
    //public static ShardKeyResolver<T> NewNotFound<T>() => static x => throw StandardError.Internal($"ShardKeyResolver not found for type {typeof(T).GetName()}.");
    public static ShardKeyResolver<T> NewNotFound<T>()
    {
        var hashBased = NewHashBased<T>();
        return x => {
            Log.LogWarning("ShardKeyResolver not found for type {TypeName}", typeof(T).GetName());
            return hashBased(x);
        };
    }

    // These properties can be set!

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RandomShard() => Random.Shared.Next();
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ForString(string? x) => x?.GetXxHash3() ?? 0;

    static ShardKeyResolvers()
    {
        // Value types
        Register<Unit>(static _ => 0);
        Register<int>(static x => x); // Used for IDiagnosticsBackend.GetShardHostId
        Register<Symbol>(static x => ForString(x.Value));
        Register<ChatId>(static x => ForString(x.Value));
        Register<PeerChatId>(static x => ForString(x.Value));
        Register<PlaceId>(static x => ForString(x.Value));
        Register<PlaceChatId>(static x => ForString(x.Value));
        Register<ChatEntryId>(static x => ForString(x.ChatId.Value));
        Register<AuthorId>(static x => ForString(x.ChatId.Value));
        Register<RoleId>(static x => ForString(x.ChatId.Value));
        Register<MentionRef>(static x => ForString(x.Target.ShardKey));
        Register<UserId>(static x => ForString(x.Value));
        Register<PrincipalId>(static x => ForString(x.ShardKey));
        Register<ContactId>(static x => ForString(x.OwnerId.Value));
        Register<NotificationId>(static x => ForString(x.UserId.Value));
        Register<MediaId>(static x => ForString(x.Value));
        Register<TranslationId>(static x => ForString(x.SourceId.ChatId.Value));
        Register<TranslationSourceId>(static x => ForString(x.ChatId.Value));
        Register<ContentId>(static x => ForString(x.TargetId.Value));
        Register<UserDeviceId>(static x => ForString(x.OwnerId.Value));
        Register<StreamId>(static x => ForString(x.Value)); // Used as a shard key in TranslationsBackend_TranslateStream
        Register<UserIdentity>(static x => ForString(x.Id));
        Register<FlowId>(static x => ForString(x.Arguments));
        Register<UploadId>(static x => ForString(x.Value));
        Register<ExplicitNotificationId>(static x => ForString(x.UserId.Value));
        Register<AliasId>(static x => ForString(x.Value));
        Register<ConversationId>(static x => ForString(x.ChatId.Value));
        Register<ExternalContactId>(static x => ForString(x.UserDeviceId.OwnerId.Value));

        // Classes
        Register<string>(ForString); // TODO(AY): String shard keys are likely a mistake -> remove this in future
        Register<Session>(x => ForString(x.Id));
        Register<ISessionCommand>(x => ForString(x.Session.Id));
        Register<FlowResumeEvent>(static x => ForString(x.FlowId.Arguments));
    }

    public static void Register<T>(ShardKeyResolver<T> resolver)
    {
        if (Registered.TryAdd(typeof(T), (ShardKeyResolver<T>)NullableResolver))
            return;

        throw StandardError.Internal(
            $"ShardKeyResolver for type {typeof(T).GetName()} is already registered.");

        int NullableResolver(T x)
            => x is not null ? resolver(x) : 0;
    }

    public static ShardKeyResolver<object?> GetUntyped(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => GenericInstanceCache.GetUnsafe<ShardKeyResolver<object?>>(typeof(UntypedFactory<>), type);

    public static ShardKeyResolver<T> Get<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        => GenericInstanceCache.GetUnsafe<ShardKeyResolver<T>>(typeof(Factory<>), typeof(T));

    public static Delegate Get(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => Unsafe.As<Delegate>(GenericInstanceCache.Get(typeof(Factory<>), type)!);

    // Nested types

    private sealed class Factory<T> : GenericInstanceFactory, IGenericInstanceFactory<ShardKeyResolver<T>>
    {
        public override ShardKeyResolver<T> Generate()
        {
            if (Registered.TryGetValue(typeof(T), out var result))
                return (ShardKeyResolver<T>)result;

            var type = typeof(T);
            if (type.IsValueType) {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                    var baseType = type.GetGenericArguments()[0];
                    if (Registered.TryGetValue(baseType, out result))
                        return (ShardKeyResolver<T>)GenericInstanceCache
                            .Get(typeof(NullableFactory<>), baseType)!;
                }
                return NotFound(type);
            }

            foreach (var baseType in type.GetAllBaseTypes(false, true)) {
                if (Registered.TryGetValue(baseType, out result))
                    return (ShardKeyResolver<T>)result;

                if (baseType is { IsInterface: true, IsGenericType: true } && baseType.GetGenericTypeDefinition() == typeof(IHasShardKey<>)) {
                    var shardKeyType = baseType.GetGenericArguments()[0];
                    return (ShardKeyResolver<T>)GenericInstanceCache
                        .Get(typeof(HasShardKeyFactory<,>), type, shardKeyType)!;
                }
            }
            return NotFound(type);
        }

        private static ShardKeyResolver<T> NotFound(Type type)
        {
            Log.LogError("ShardKeyResolvers: shard key type: {Type}, requester: {Requester}",
                type.GetName(), "GenericInstanceFactory");
            return NewNotFound<T>();
        }
    }

    private sealed class NullableFactory<T> : GenericInstanceFactory, IGenericInstanceFactory<ShardKeyResolver<T?>>
        where T : struct
    {
        public override ShardKeyResolver<T?> Generate()
        {
            if (!Registered.TryGetValue(typeof(T), out var baseResolver))
                return NewNotFound<T?>();

            return NewNullable((ShardKeyResolver<T>)baseResolver);
        }
    }

    private sealed class HasShardKeyFactory<T, TShardKey>
        : GenericInstanceFactory, IGenericInstanceFactory<ShardKeyResolver<T>>
        where T : IHasShardKey<TShardKey>
    {
        public override ShardKeyResolver<T> Generate()
        {
            var resolver = Get<TShardKey>();
            if (typeof(T).IsValueType)
                return x => resolver.Invoke(x.ShardKey);

            return x => ReferenceEquals(x, null)
                ? 0
                : resolver.Invoke(x.ShardKey);
        }
    }

    private sealed class UntypedFactory<T>
        : GenericInstanceFactory, IGenericInstanceFactory<ShardKeyResolver<object?>>
    {
        public override ShardKeyResolver<object?> Generate()
            => Get<T>().ToUntyped();
    }
}
