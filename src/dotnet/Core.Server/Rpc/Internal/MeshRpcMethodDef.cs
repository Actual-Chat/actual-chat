using ActualLab.Rpc;

namespace ActualChat.Rpc.Internal;

public class MeshRpcMethodDef : RpcMethodDef
{
    // ReSharper disable once ConvertToPrimaryConstructor
    public MeshRpcMethodDef(RpcServiceDef service, MethodInfo methodInfo)
        : base(service, methodInfo)
    { }
}
