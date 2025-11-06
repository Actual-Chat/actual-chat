using ActualLab.Rpc;

namespace ActualChat.Rpc.Internal;

public class MeshRpcServiceDef : RpcServiceDef
{
    // ReSharper disable once ConvertToPrimaryConstructor
    public MeshRpcServiceDef(RpcHub hub, RpcServiceBuilder service)
        : base(hub, service)
    {
        var backendServiceDefs = Hub.Services.BackendServiceDefs();
        IsBackend = backendServiceDefs.Contains(Type);
    }
}
