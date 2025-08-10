using ActualChat.Sharding;
using ActualLab.Interception;
using ActualLab.Rpc;

namespace ActualChat.Rpc;

public delegate MeshRef MeshRpcCallRouter(RpcMethodDef methodDef, ArgumentList arguments, ShardScheme shardScheme);

public static class RpcMethodDefExt
{
    private static readonly MethodInfo RouteCallMethod = typeof(RpcMethodDefExt)
        .GetMethod(nameof(RouteCall), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly ConcurrentDictionary<RpcMethodDef, MeshRpcCallRouter> Cache = new();

    public static MeshRpcCallRouter GetCallRouter(this RpcMethodDef methodDef)
        => Cache.GetOrAdd(methodDef,
            static (methodDef) => {
                var parameterTypes = methodDef.ParameterTypes;
                if (parameterTypes.Length == 0)
                    return RouteCall<ThisNodeRef>; // No parameters = we'll route it to the local node

                var arg0Type = parameterTypes[0];
                return RouteCallMethod.MakeGenericMethod(arg0Type).CreateDelegate<MeshRpcCallRouter>();
            });

    // Private methods

    private static MeshRef RouteCall<T>(
        RpcMethodDef methodDef,
        ArgumentList arguments,
        ShardScheme shardScheme)
    {
        var meshRef = MeshRefResolvers.Get<T>(new Requester(methodDef)).Invoke(arguments.Get<T>(0));
        return meshRef.WithSchemeIfUndefined(shardScheme);
    }
}
