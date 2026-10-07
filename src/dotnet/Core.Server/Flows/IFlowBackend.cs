using ActualChat.Attributes;
using ActualChat.Flows.Infrastructure;
using ActualLab.CommandR.Operations;
using ActualLab.Rpc;
using System.Text;

namespace ActualChat.Flows;

[BackendService(nameof(HostRole.FlowsBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.FlowsBackend))]
public interface IFlowBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<IFlowData?> TryGetData(FlowId flowId, CancellationToken cancellationToken);
    // Every inbox change primes it before its command completes, and the change, the flow's resume
    // and its commits all run on the flow's shard owner - so a resume always reads it up to date
    [ComputeMethod]
    Task<ApiArray<FlowInboxMessage>> GetInbox(FlowId flowId, CancellationToken cancellationToken);
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
    [CommandHandler]
    Task OnChangeInbox(Flows_ChangeInbox command, CancellationToken cancellationToken);
}

// This command:
// - Is guaranteed to always run locally (see the `IHasNodeRef` implementation),
//   that's why a part of fields there are non-serializable.
// - Doesn't run invalidation block (it's an `IDelegatingCommand`).
// ReSharper disable once InconsistentNaming
public sealed record Flows_Store(FlowId FlowId, long? ExpectedVersion = null)
    : IDelegatingCommand<long>, IBackendCommand, IHasNodeRef
{
    public Flow? Flow { get; init; }
    public OperationEvent[]? Events { get; init; }
    // The inbox changes the flow made, stored in the same transaction as the flow itself
    public FlowInboxDiff? InboxDiff { get; init; }
    // Set by a caller for which a skipped store is an ordinary outcome, e.g., Start losing a race to another Start
    public bool IsSkipExpected { get; init; }

    // IHasNodeRef implementation - always routes the command to the local node
    NodeRef IHasNodeRef.NodeRef => NodeRef.ThisNodeAlias;

    // Flow and Events are the whole payload - dumping them into a log line is what
    // this command used to carry INotLogged for
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("FlowId = ").Append(FlowId)
            .Append(", ExpectedVersion = ").Append(ExpectedVersion)
            .Append(", Flow = ").Append(Flow?.GetType().GetName())
            .Append(", Events = ").Append(Events?.Length ?? 0)
            .Append(", InboxDiff = ")
            .Append(InboxDiff is { } d ? $"+{d.Added.Count}/-{d.RemovedIds.Count}" : "none");
        return true;
    }
}

// This command:
// - Is guaranteed to always run locally (see the `IHasNodeRef` implementation),
//   that's why a part of fields there are non-serializable.
// - Doesn't run invalidation block (it's an `IDelegatingCommand`).
// ReSharper disable once InconsistentNaming
public sealed record Flows_ScheduleResume(FlowResumeEvent Item, FlowResumeEvent[]? Items = null)
    : IDelegatingCommand<long>, IBackendCommand, IHasNodeRef
{
    // IHasNodeRef implementation - always routes the command to the local node
    NodeRef IHasNodeRef.NodeRef => NodeRef.ThisNodeAlias;

    // The events are the whole payload - see Flows_Store
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Item = ").Append(Item.FlowId)
            .Append(", Items = ").Append(Items?.Length ?? 0);
        return true;
    }
}

// Adds and removes inbox messages of an IInboxProcessingFlow atomically; the added ones get their Ids
// here. With MustResume, the flow's resume is scheduled in the same operation. Posting it as an
// operation event delivers it once the poster's own operation commits. A completed flow ignores it.
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Flows_ChangeInbox(
    [property: DataMember(Order = 0), Key(0)] FlowId FlowId,
    [property: DataMember(Order = 1), Key(1)] FlowInboxDiff Diff
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [DataMember(Order = 2), Key(2)] public bool MustResume { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => FlowId.ShardKey;

    public static Flows_ChangeInbox Post(FlowId flowId, params IEnumerable<object> payloads)
        => new(flowId, new FlowInboxDiff(payloads.Select(p => FlowInboxMessage.New(p)).ToApiArray(), [])) {
            MustResume = true,
        };

    public static Flows_ChangeInbox Remove(FlowId flowId, params IEnumerable<long> ids)
        => new(flowId, new FlowInboxDiff([], ids.ToApiArray()));
}
