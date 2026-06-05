using ActualChat.Attributes;
using ActualChat.Flows.Infrastructure;
using ActualLab.CommandR.Operations;
using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat.Flows;

[BackendService(nameof(HostRole.FlowsBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.FlowsBackend))]
public interface IFlowBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<IFlowData?> TryGetData(FlowId flowId, CancellationToken cancellationToken);
    // Regular RPC method!
    Task<IFlowData> Start(FlowId flowId, long? expectedVersion, CancellationToken cancellationToken);
    // Regular RPC methods! Pinned to a single node via ShardKeyResolver<Unit> (the leading Unit arg).
    Task<FlowTypeStat[]> ListStats(Unit unit, CancellationToken cancellationToken);
    Task<FlowSummary[]> List(Unit unit, FlowsQuery query, CancellationToken cancellationToken);

    // The `long` result in any of the methods below return is DbFlow/FlowData.Version
    [CommandHandler]
    Task<long> OnResume(FlowResumeEvent resumeEvent, CancellationToken cancellationToken);
    [CommandHandler]
    Task<long> OnStore(Flows_Store command, CancellationToken cancellationToken);
    [CommandHandler]
    [RpcMethod(LocalExecutionMode = RpcLocalExecutionMode.Unconstrained)]
    Task OnScheduleResume(Flows_ScheduleResume command, CancellationToken cancellationToken);
}

// This command:
// - Is guaranteed to always run locally (see the `IHasNodeRef` implementation),
//   that's why a part of fields there are non-serializable.
// - Doesn't run invalidation block (it's an `IDelegatingCommand`).
// ReSharper disable once InconsistentNaming
public sealed record Flows_Store(FlowId FlowId, long? ExpectedVersion = null)
    : IDelegatingCommand<long>, IBackendCommand, IHasNodeRef, INotLogged
{
    public Flow? Flow { get; init; }
    public OperationEvent[]? Events { get; init; }

    // IHasNodeRef implementation - always routes the command to the local node
    NodeRef IHasNodeRef.NodeRef => NodeRef.ThisNodeAlias;
}

// This command:
// - Is guaranteed to always run locally (see the `IHasNodeRef` implementation),
//   that's why a part of fields there are non-serializable.
// - Doesn't run invalidation block (it's an `IDelegatingCommand`).
// ReSharper disable once InconsistentNaming
public sealed record Flows_ScheduleResume(FlowResumeEvent Item, FlowResumeEvent[]? Items = null)
    : IDelegatingCommand<long>, IBackendCommand, IHasNodeRef, INotLogged
{
    // IHasNodeRef implementation - always routes the command to the local node
    NodeRef IHasNodeRef.NodeRef => NodeRef.ThisNodeAlias;
}
