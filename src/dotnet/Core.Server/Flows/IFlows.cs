using ActualChat.Attributes;
using ActualLab.CommandR.Operations;
using ActualChat.Hosting;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Flows;

[BackendService(nameof(HostRole.OneServer), ServiceMode.Local, Priority = 1)]
[BackendService(nameof(HostRole.FlowsBackend), ServiceMode.Distributed)]
[BackendClient(nameof(HostRole.FlowsBackend))]
public interface IFlows : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Flow?> TryGet(FlowId flowId, CancellationToken cancellationToken = default);
    // Regular method!
    Task<Flow> Start(FlowId flowId, CancellationToken cancellationToken = default);

    // The `long` result in any of the methods below return is DbFlow/FlowData.Version
    // Regular method!
    Task<long> OnEvent(FlowId flowId, IFlowEvent evt, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task<long> OnStore(Flows_Store command, CancellationToken cancellationToken = default);
}

// This is a special command always executed locally. It is never sent to remote peers.
// Search for "MeshRefResolvers.Register<Flows_Store>" to see how it works.
// ReSharper disable once InconsistentNaming
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public partial record Flows_Store(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long? ExpectedVersion = null
) : ICommand<long>, IBackendCommand, IHasShardKey<FlowId>, INotLogged
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Flow? Flow { get; init; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public OperationEvent[]? AddEvents { get; init; }

    // IHasShardKey<FlowId>
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public FlowId ShardKey => FlowId;
}
