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

public class FlowBackend(IServiceProvider services) : ShardedDbServiceBase<FlowsDbContext>(services), IFlows
{
    private readonly AsyncLockSet<FlowId> _resumeLocks = new();
    private readonly ILruCache<FlowId, Flow?> _cache = new ConcurrentLruCache<FlowId, Flow?>(1024);

    private FlowRegistry Registry { get; } = services.GetRequiredService<FlowRegistry>();
    private FlowHost Host { get; } = services.GetRequiredService<FlowHost>();
    private IDbEntityResolver<string, DbFlow> EntityResolver { get; } = services.DbEntityResolver<string, DbFlow>();
    private IByteSerializer Serializer { get; } = TypeDecoratingByteSerializer.Default;
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Flows);

    public IRetryPolicy GetOrStartRetryPolicy { get; init; } = new RetryPolicy(3, RetryDelaySeq.Exp(0.25, 1));

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
        if (Invalidation.IsActive) {
            _ = TryGet(flowId, default);
            return default;
        }

        flowId.Require();
        var flow = command.Flow.Require();
        var legacyFlow = flow as LegacyFlow;
        var context = CommandContext.GetCurrent();

        var shard = DbHub.ShardResolver.Resolve(flowId);
        var dbContext = await DbHub.CreateOperationDbContext(shard, cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(true);

        await dbContext.Set<DbFlow>().LockShared(flowId, cancellationToken).ConfigureAwait(false);
        var dbFlow = await dbContext.Set<DbFlow>().ForUpdate()
            .FirstOrDefaultAsync(x => Equals(x.Id, flowId.Value), cancellationToken)
            .ConfigureAwait(false);

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
        foreach (var e in command.AddEvents ?? [])
            context.Operation.AddEvent(e);

        await dbContext.Set<DbFlow>().Lock(flowId, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbFlow.Version;
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
        var data = dbFlow?.Data;
        if (data == null || data.Length == 0)
            return _cache[flowId] = null!; // Update cache

        flow = Deserialize(data);
        if (flow is LegacyFlow legacyFlow)
            legacyFlow.Initialize(flowId, dbFlow!.Version, dbFlow.Step, dbFlow.HardResumeAt);
        else
            flow.Initialize(flowId, dbFlow!.Version);
        return _cache[flowId] = flow;  // Update cache
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
