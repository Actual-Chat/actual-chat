using ActualChat.Attributes;
using ActualLab.CommandR.Operations;
using ActualChat.Hosting;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Flows;

[BackendService(nameof(HostRole.FlowsBackend), ServiceMode.Distributed)]
[BackendClient(nameof(HostRole.FlowsBackend))]
public interface IFlows : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<IFlowData?> TryGetData(FlowId flowId, CancellationToken cancellationToken);
    // Regular RPC method!
    Task<Flow> Start(FlowId flowId, CancellationToken cancellationToken);

    // The `long` result in any of the methods below return is DbFlow/FlowData.Version
    [CommandHandler]
    Task<long> OnEvent(IFlowEvent command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<long> OnStore(Flows_Store command, CancellationToken cancellationToken);
}

// This command:
// - Is guaranteed to always run locally (see the `IHasNodeRef` implementation),
//   that's why a part of fields there are non-serializable.
// - Doesn't run invalidation block (it's an `IDelegatingCommand`).
// ReSharper disable once InconsistentNaming
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public partial record Flows_Store(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long? ExpectedVersion = null
) : IDelegatingCommand<long>, IBackendCommand, IHasNodeRef, INotLogged
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Flow? Flow { get; init; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public OperationEvent[]? Events { get; init; }

    // IHasNodeRef implementation - always routes the command to the local node
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    NodeRef IHasNodeRef.NodeRef => NodeRef.ThisNodeAlias;
}
