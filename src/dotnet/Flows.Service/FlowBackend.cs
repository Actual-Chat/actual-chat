using ActualChat.Db;
using ActualChat.Flows.Db;
using ActualChat.Flows.Infrastructure;
using ActualLab.Diagnostics;
using ActualLab.Fusion.EntityFramework;
using ActualLab.IO;
using ActualLab.Locking;
using ActualLab.Resilience;
using ActualLab.Rpc;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Flows;

public class FlowBackend : ShardedDbServiceBase<FlowsDbContext>, IFlows
{
    private readonly AsyncLockSet<FlowId> _resumeLocks = new();
    private readonly ILruCache<FlowId, Flow?> _cache = new ConcurrentLruCache<FlowId, Flow?>(1024);

    public FlowBackend(IServiceProvider services) : base(services)
    {
        Registry = services.GetRequiredService<FlowRegistry>();
        Host = services.GetRequiredService<FlowHost>();
        EntityResolver = services.DbEntityResolver<string, DbFlow>();
        _ = ClearCacheRetryPolicy
            .Apply(async ct => {
                // TODO(AY): Split cache to per-shard caches and make them track their own changes
                var shardOwnershipChanges = ShardOwner.State.Computed.ChangesUntyped(FixedDelayer.YieldUnsafe, ct);
                await foreach (var _ in shardOwnershipChanges.ConfigureAwait(false))
                    _cache.Clear();
                // ReSharper disable once ExplicitCallerInfoArgument
            }, new RetryLogger(Log, "ClearCache"), ShardOwner.StopToken)
            .ConfigureAwait(false);
    }

    private FlowRegistry Registry { get; }
    private FlowHost Host { get; }
    private IDbEntityResolver<string, DbFlow> EntityResolver { get; }
    private IByteSerializer Serializer { get; } = TypeDecoratingByteSerializer.Default;
    private IRetryPolicy StartRetryPolicy { get; init; } = new RetryPolicy(3, RetryDelaySeq.Exp(0.25, 1)) {
        // RpcRerouteException is a special case: it's thrown when the shard is not owned by the current node
        RetryOn = (e, transiency) => e is not RpcRerouteException && transiency is not Transiency.Terminal,
    };
    private IRetryPolicy ClearCacheRetryPolicy { get; init; } = new RetryPolicy(RetryDelaySeq.Fixed(0.1));
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Flows);

    // [ComputeMethod]
    public virtual Task<Flow?> TryGet(FlowId flowId, CancellationToken cancellationToken)
        => TryGetImpl(flowId, cancellationToken);

    // Regular RPC method!
    public virtual Task<Flow> Start(FlowId flowId, CancellationToken cancellationToken)
    {
        var flowType = Registry.TypeByName[flowId.Name];
        return StartRetryPolicy.RunIsolated(async ct => {
            // RunIsolated also ensures the code below doesn't
            // produce any dependencies for the caller, even though it calls TryGet.
            var flow = await TryGet(flowId, ct).ConfigureAwait(false);
            if (flow is not null)
                return flow;

            // Ensure the shard for this flow is owned by the current node
            var shardOwnership = await ShardOwner.RequireOwnedOrReroute(flowId, ct).ConfigureAwait(false);
            using var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);
            var linkedToken = linkedCts.Token;
            try {
                flow = (Flow)flowType.CreateInstance();
                var legacyFlowImpl = flow as ILegacyFlowImpl;
                var flowImpl = (IFlowImpl)flow;
                if (legacyFlowImpl is not null)
                    legacyFlowImpl.Initialize(flowId, 0, LegacyFlowSteps.Starting);
                else
                    flowImpl.Initialize(flowId, 0);

                // Flow_Store is guaranteed to run locally or fail
                var storeCommand = new Flows_Store(flow.Id, 0) { Flow = flow };
                var version = await Commander.Call(storeCommand, true, linkedToken).ConfigureAwait(false);
                if (legacyFlowImpl is not null)
                    legacyFlowImpl.Initialize(flowId, version, LegacyFlowSteps.Starting);
                else
                    flowImpl.Initialize(flowId, version);
                return flow;
            }
            catch (Exception e) when (e.IsCancellationOf(linkedToken)) {
                throw RpcRerouteException.MustReroute(); // StartRetryPolicy doesn't retry on this one
            }
        }, new RetryLogger(Log), cancellationToken);
    }

    // The `long` it returns is DbFlow/FlowData.Version
    // [CommandHandler]
    public virtual Task<long> OnEvent(IFlowEvent command, CancellationToken cancellationToken)
    {
        var flowId = command.FlowId;
        DebugLog?.LogDebug("OnEvent: `{FlowId}` <- {Event}", flowId, command);
        var flowType = Registry.TypeByName[flowId.Name];
        return flowType.IsAssignableTo(typeof(LegacyFlow))
            ? Host.ProcessEvent(flowId, command, cancellationToken)
            : ProcessEvent();

        async Task<long> ProcessEvent() {
            if (command is not FlowResumeEvent)
                throw StandardError.Internal($"Unsupported event type: {command.GetType()}.");

            // Ensure the shard for this flow is owned by the current node
            var shardOwnership = await ShardOwner.RequireOwnedOrReroute(flowId, cancellationToken).ConfigureAwait(false);
            using var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);
            var linkedToken = linkedCts.Token;

            // Ensure the resume logic below doesn't run concurrently for the same flow
            await _resumeLocks.Lock(flowId, linkedToken).ConfigureAwait(false);

            // Get the flow, clone it
            var flow = await this.Get(flowId, linkedToken).ConfigureAwait(false);
            flow = flow.Clone();

            // Run the Resume method on it
            var runtime = new FlowRuntime(flow, Services, linkedToken);
            await ((IFlowImpl)flow).Resume(runtime, linkedToken).ConfigureAwait(false);
            return flow.Version;
        }
    }

    // The `long` it returns is DbFlow/FlowData.Version
    // [CommandHandler]
    public virtual async Task<long> OnStore(Flows_Store command, CancellationToken cancellationToken)
    {
        // NOTE(AY): this command handler:
        // - Is guaranteed to always run locally (see `IHasNodeRef` in Flows_Store).
        // - Doesn't run in the invalidation mode (it's an `IDelegatingCommand`).
        // Nevertheless, it has the invalidation logic - see the `AddCompletionHandler` call below.

        var (flowId, expectedVersion) = command;
        var context = CommandContext.GetCurrent();

        flowId.Require();
        var flow = command.Flow;
        var legacyFlow = flow as LegacyFlow;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(true);

        await dbContext.Set<DbFlow>().LockShared(flowId, cancellationToken).ConfigureAwait(false);
        var dbFlow = await dbContext.Set<DbFlow>().ForUpdate()
            .FirstOrDefaultAsync(x => Equals(x.Id, flowId.Value), cancellationToken)
            .ConfigureAwait(false);

        var isLegacyRemoval = legacyFlow?.Step == LegacyFlowSteps.Removed;
        if (flow is not null && !isLegacyRemoval) {
            if (dbFlow is null) { // Create
                dbFlow = new DbFlow() {
                    Id = flowId,
                    Version = VersionGenerator.NextVersion(),
                    HardResumeAt = legacyFlow?.HardResumeAt,
                    Step = legacyFlow?.Step.Value ?? "",
                    Data = Serialize(flow),
                };
                dbContext.Add(dbFlow);

                // Any new flow requires a resume or start event
                if (legacyFlow is not null) {
                    if (legacyFlow.Step != LegacyFlowSteps.Starting)
                        throw StandardError.Internal("LegacyFlow.Step should be 'Starting' for a new LegacyFlow.");

                    context.Operation.AddEvent(new LegacyFlowStartEvent(flowId));
                }
                else
                    context.Operation.AddEvent(new FlowResumeEvent(flowId));
            }
            else { // Update
                VersionChecker.RequireExpected(dbFlow.Version, expectedVersion);
                dbFlow.Version = VersionGenerator.NextVersion(dbFlow.Version);
                dbFlow.HardResumeAt = legacyFlow?.HardResumeAt;
                dbFlow.Step = legacyFlow?.Step.Value ?? "";
                dbFlow.Data = Serialize(flow);
            }
        }
        else {
            // Remove
            if (dbFlow is null) {
                // Nothing to remove, but maybe a version check is needed?
                VersionChecker.RequireExpected(0L, expectedVersion);
            }
            else {
                VersionChecker.RequireExpected(dbFlow.Version, expectedVersion);
                dbContext.Remove(dbFlow);
                dbFlow = null;
            }
        }
        foreach (var e in command.Events ?? [])
            context.Operation.AddEvent(e);

        // We don't store DbOperation entries for flow updates,
        // coz invalidation must be handled by the local node only.
        // See AddCompletionHandler below.
        context.Operation.MustStore(false);
        context.Operation.AddCompletionHandler(scope => {
            // Update cache to avoid the DB hit in TryGet
            _cache[flowId] = Materialize(flowId, dbFlow);
            // Invalidate TryGet cache
            using (Invalidation.Begin())
                _ = TryGet(flowId, default);
            return Task.CompletedTask;
        });

        await dbContext.Set<DbFlow>().Lock(flowId, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbFlow?.Version ?? 0L;
    }

    // Private methods methods

    private async Task<Flow?> TryGetImpl(FlowId flowId, CancellationToken cancellationToken = default)
    {
        await ShardOwner.RequireOwnedOrReroute(flowId, cancellationToken).ConfigureAwait(false);
        // Check cache
        if (_cache.TryGetValue(flowId, out var flow))
            return flow;

        // Read the ground truth
        var dbFlow = await EntityResolver.Get(flowId.Value, cancellationToken).ConfigureAwait(false);
        flow = Materialize(flowId, dbFlow);
        return _cache[flowId] = flow;  // Update cache
    }

    [return: NotNullIfNotNull(nameof(dbFlow))]
    private Flow? Materialize(FlowId flowId, DbFlow? dbFlow)
    {
        var data = dbFlow?.Data;
        if (data == null || data.Length == 0)
            return null; // Update cache

        var flow = Deserialize(data);
        if (flow is ILegacyFlowImpl legacyFlowImpl)
            legacyFlowImpl.Initialize(flowId, dbFlow!.Version, dbFlow.Step, dbFlow.HardResumeAt);
        else if (flow is IFlowImpl flowImpl)
            flowImpl.Initialize(flowId, dbFlow!.Version);
        else
            throw StandardError.Internal($"Invalid flow type: {flow.GetType()}");
        return flow;
    }

    private byte[] Serialize(Flow flow)
    {
        using var buffer = new ArrayPoolBuffer<byte>(256);
        Serializer.Write(buffer, flow, flow.GetType());
        return buffer.WrittenSpan.ToArray();
    }

    private Flow Deserialize(byte[]? data)
        => (Flow)Serializer.Read(data, typeof(Flow), out _)!;
}
