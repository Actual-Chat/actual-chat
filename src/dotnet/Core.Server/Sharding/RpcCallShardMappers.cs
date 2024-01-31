using ActualLab.Interception;
using ActualLab.Rpc;

namespace ActualChat;

public delegate int RpcCallShardMapper(RpcMethodDef methodDef, ArgumentList arguments, Sharding sharding);
public delegate RpcCallShardMapper? RpcCallShardMapperProvider(RpcMethodDef methodDef);

public sealed class RpcCallShardMappers(IServiceProvider services)
{
    private static readonly MethodInfo DefaultMapperImplMethod = typeof(ShardKeyHandlers)
        .GetMethod(nameof(DefaultMapperImpl), BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly RpcCallShardMapperProvider[] _providers
        = services.GetServices<RpcCallShardMapperProvider>().ToArray();
    private readonly ConcurrentDictionary<RpcMethodDef, RpcCallShardMapper> _cachedMappers = new();

    public RpcCallShardMapperProvider DefaultProvider { get; init; } = DefaultProviderImpl;

    public RpcCallShardMapper this[RpcMethodDef methodDef]
        => _cachedMappers.GetOrAdd(methodDef,
            static (methodDef1, self) => {
                foreach (var p in self._providers) {
                    var shardMapper = p.Invoke(methodDef1);
                    if (shardMapper != null)
                        return shardMapper;
                }
                return self.DefaultProvider.Invoke(methodDef1)
                    ?? throw StandardError.Internal("DefaultProvider should never return null.");
            },
            this);

    private static RpcCallShardMapper DefaultProviderImpl(RpcMethodDef methodDef)
    {
        if (methodDef.ArgumentListType == typeof(ArgumentList0))
            return DefaultMapperImpl<Unit>;

        var arg0Type = methodDef.ArgumentListType.GetGenericArguments()[0];
        return DefaultMapperImplMethod.MakeGenericMethod(arg0Type).CreateDelegate<RpcCallShardMapper>();
    }

    private static int DefaultMapperImpl<T>(RpcMethodDef methodDef, ArgumentList arguments, Sharding sharding)
        => ShardKeyHandlers.Map(arguments.Get<T>(0), sharding);
}
