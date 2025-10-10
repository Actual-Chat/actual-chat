using ActualChat.Db;
using ActualChat.Flows.Db;
using ActualChat.Flows.Infrastructure;
using ActualLab.Diagnostics;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Interception;
using ActualLab.IO;
using ActualLab.Locking;
using ActualLab.Resilience;
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
    private IRetryPolicy GetOrStartRetryPolicy { get; init; } = new RetryPolicy(3, RetryDelaySeq.Exp(0.25, 1));
    private IRetryPolicy ClearCacheRetryPolicy { get; init; } = new RetryPolicy(RetryDelaySeq.Fixed(0.1));
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Flows);

    // [ComputeMethod]
    public virtual Task<Flow?> TryGet(FlowId flowId, CancellationToken cancellationToken = default)
        => TryGetImpl(flowId, cancellationToken);

    // Regular method!
    public virtual Task<Flow> Start(FlowId flowId, CancellationToken cancellationToken = default)
    {
        var flowType = Registry.TypeByName[flowId.Name];
        return GetOrStartRetryPolicy.RunIsolated(async ct => {
            // RunIsolated also ensures the code below doesn't
            // produce any dependencies for the caller, even though it calls TryGet.
            var flow = await TryGet(flowId, ct).ConfigureAwait(false);
            if (flow is not null)
                return flow;

            var legacyFlow = (LegacyFlow)flowType.CreateInstance();
            legacyFlow.Initialize(flowId, 0, LegacyFlowSteps.Starting);
            var storeCommand = new Flows_Store(legacyFlow.Id, 0) { Flow = legacyFlow };
            var version = await Commander.Call(storeCommand, true, ct).ConfigureAwait(false);
            legacyFlow.Initialize(flowId, version, LegacyFlowSteps.Starting);
            return legacyFlow;
        }, new RetryLogger(Log), cancellationToken);
    }

    // The `long` it returns is DbFlow/FlowData.Version
    [ProxyIgnore] // Regular method!
    public virtual Task<long> OnEvent(FlowId flowId, IFlowEvent evt, CancellationToken cancellationToken = default)
    {
        DebugLog?.LogDebug("OnEvent: `{FlowId}` <- {Event}", flowId, evt);
        return Host.ProcessEvent(flowId, evt, cancellationToken);
    }

    // The `long` it returns is DbFlow/FlowData.Version
    // [CommandHandler]
    public virtual async Task<long> OnStore(Flows_Store command, CancellationToken cancellationToken = default)
    {
        var (flowId, expectedVersion) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            // This code runs only on the local node, see context.Operation.MustCreate(false) below
            var invDbFlow = context.Operation.Items.KeylessGet<DbFlow>();
            var invFlow = Materialize(flowId, invDbFlow);
            _cache[flowId] = invFlow; // Update cache to skip DB hit in TryGet
            _ = TryGet(flowId, default);
            return default;
        }

        flowId.Require();
        var flow = command.Flow;
        var legacyFlow = flow as LegacyFlow;

        var shard = DbHub.ShardResolver.Resolve(flowId);
        var dbContext = await DbHub.CreateOperationDbContext(shard, cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(true);

        await dbContext.Set<DbFlow>().LockShared(flowId, cancellationToken).ConfigureAwait(false);
        var dbFlow = await dbContext.Set<DbFlow>().ForUpdate()
            .FirstOrDefaultAsync(x => Equals(x.Id, flowId.Value), cancellationToken)
            .ConfigureAwait(false);

        if (flow is not null) {
            if (dbFlow is null) { // Create
                if (legacyFlow is not null) {
                    if (legacyFlow.Step != LegacyFlowSteps.Starting)
                        throw StandardError.Internal("LegacyFlow.Step should be 'Starting' for a new LegacyFlow.");

                    context.Operation.AddEvent(new LegacyFlowStartEvent(flowId));
                }
                dbFlow = new DbFlow() {
                    Id = flowId,
                    Version = VersionGenerator.NextVersion(),
                    HardResumeAt = legacyFlow?.HardResumeAt,
                    Step = legacyFlow?.Step.Value ?? "",
                    Data = Serialize(flow),
                };
                dbContext.Add(dbFlow);
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
        foreach (var e in command.AddEvents ?? [])
            context.Operation.AddEvent(e);

        // We don't store DbOperation entries for flow updates - we rely on sharding instead.
        context.Operation.MustCreate(false);
        context.Operation.Items.KeylessSet(dbFlow);

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
        if (flow is LegacyFlow legacyFlow)
            legacyFlow.Initialize(flowId, dbFlow!.Version, dbFlow.Step, dbFlow.HardResumeAt);
        else
            flow.Initialize(flowId, dbFlow!.Version);
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
