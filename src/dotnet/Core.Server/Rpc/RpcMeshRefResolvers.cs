using ActualLab.Interception;
using ActualLab.Rpc;

namespace ActualChat.Rpc;

public delegate MeshRef RpcMeshRefResolver(RpcMethodDef methodDef, ArgumentList arguments, ShardScheme shardScheme);
public delegate RpcMeshRefResolver? RpcMeshRefResolverProvider(RpcMethodDef methodDef);

public sealed class RpcMeshRefResolvers(IServiceProvider services)
{
    private static readonly MethodInfo DefaultResolverImplMethod = typeof(RpcMeshRefResolvers)
        .GetMethod(nameof(DefaultResolverImpl), BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly RpcMeshRefResolverProvider[] _providers
        = services.GetServices<RpcMeshRefResolverProvider>().ToArray();
    private readonly ConcurrentDictionary<RpcMethodDef, RpcMeshRefResolver> _cachedResolvers = new();

    public RpcMeshRefResolverProvider DefaultProvider { get; init; } = DefaultProviderImpl;

    public RpcMeshRefResolver this[RpcMethodDef methodDef]
        => _cachedResolvers.GetOrAdd(methodDef,
            static (methodDef1, self) => {
                foreach (var p in self._providers) {
                    var resolver = p.Invoke(methodDef1);
                    if (resolver != null)
                        return resolver;
                }
                return self.DefaultProvider.Invoke(methodDef1)
                    ?? throw StandardError.Internal("DefaultProvider should never return null.");
            },
            this);

    private static RpcMeshRefResolver DefaultProviderImpl(RpcMethodDef methodDef)
    {
        var parameterTypes = methodDef.ParameterTypes;
        if (parameterTypes.Length == 0)
            return DefaultResolverImpl<Unit>;

        var arg0Type = parameterTypes[0];
        return DefaultResolverImplMethod.MakeGenericMethod(arg0Type).CreateDelegate<RpcMeshRefResolver>();
    }

    private static MeshRef DefaultResolverImpl<T>(
        RpcMethodDef methodDef,
        ArgumentList arguments,
        ShardScheme shardScheme)
    {
        var meshRef = MeshRefResolvers.Get<T>(new Requester(methodDef)).Invoke(arguments.Get<T>(0));
        return meshRef.WithSchemeIfUndefined(shardScheme);
    }
}
