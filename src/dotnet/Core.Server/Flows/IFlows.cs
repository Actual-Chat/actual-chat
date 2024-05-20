using ActualChat.Attributes;
using ActualLab.CommandR.Operations;
using ActualChat.Flows.Infrastructure;
using ActualChat.Hosting;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Flows;

[BackendService(nameof(HostRole.OneServer), ServiceMode.Local, Priority = 1)]
[BackendService(nameof(HostRole.FlowsBackend), ServiceMode.Server)] // TBD: -> Hybrid
[BackendClient(nameof(HostRole.FlowsBackend))]
public interface IFlows : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<FlowData> GetData(FlowId flowId, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<Flow?> Get(FlowId flowId, CancellationToken cancellationToken = default);
    // Regular method!
    Task<Flow> GetOrStart(FlowId flowId, CancellationToken cancellationToken = default);

    // Regular method!
    Task<long> OnEvent(FlowId flowId, object? evt, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task<long> OnStore(Flows_Store command, CancellationToken cancellationToken = default);
}

// ReSharper disable once InconsistentNaming
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public partial record Flows_Store(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long? ExpectedVersion = null
) : ICommand<long>, IBackendCommand, INotLogged
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Flow? Flow { get; init; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Action<Operation>? EventBuilder { get; init; }
}
