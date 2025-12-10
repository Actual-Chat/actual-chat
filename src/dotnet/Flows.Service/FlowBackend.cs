using ActualChat.Db;
using ActualChat.Flows.Db;
using ActualChat.Flows.Infrastructure;
using ActualLab.Diagnostics;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Locking;
using ActualLab.Resilience;
using ActualLab.Rpc;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Flows;

public class FlowBackend : ShardedDbServiceBase<FlowsDbContext>, IFlows
{
    private readonly AsyncLockSet<FlowId> _resumeLocks = new();
    private readonly ILruCache<FlowId, IFlowData?> _cache = new ConcurrentLruCache<FlowId, IFlowData?>(1024);

    // Services
    private FlowRegistry Registry { get; }
    private FlowHost Host { get; }
    private IDbEntityResolver<string, DbFlow> EntityResolver { get; }
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Flows);

    // Properties
    private IRetryPolicy ResumeRetryPolicy { get; init; } = new RetryPolicy(10, RetryDelaySeq.Exp(0.25, 3)) {
        // RpcRerouteException is a special case: it's thrown when the shard is not owned by the current node
        RetryOn = (e, transiency) => e is not RpcRerouteException && transiency is not Transiency.Terminal,
    };

    public FlowBackend(IServiceProvider services) : base(services)
    {
        Registry = services.GetRequiredService<FlowRegistry>();
        Host = services.GetRequiredService<FlowHost>();
        EntityResolver = services.DbEntityResolver<string, DbFlow>();
        var stopToken = ShardOwner.StopToken;
        foreach (var shardIndex in ShardScheme.ShardIndexes) {
            var asyncState = ShardOwner.GetShardState(shardIndex).AsyncState;
            // Start the cleaner task for shardIndex, which cleans it on any ownership state change
            _ = Task.Run(async () => {
                while (true) {
                    asyncState = await asyncState.WhenNext(stopToken).ConfigureAwait(false);
                    _cache.Clear();
                }
            }, CancellationToken.None);
        }
    }

    // [ComputeMethod]
    public virtual async Task<IFlowData?> TryGetData(FlowId flowId, CancellationToken cancellationToken)
    {
        var flowType = Registry.TypeByName[flowId.Name];

        // Check the in-memory cache first
        if (_cache.TryGetValue(flowId, out var flowData))
            return flowData;

        // Read the ground truth
        var dbFlow = await EntityResolver.Get(flowId.Value, cancellationToken).ConfigureAwait(false);
        flowData = dbFlow?.ToFlowData(flowType, flowId);
        return _cache[flowId] = flowData;  // Update the in-memory cache
    }

    // Regular RPC method!
    public virtual async Task<IFlowData> Start(FlowId flowId, long? expectedVersion, CancellationToken cancellationToken)
    {
        var flowType = Registry.TypeByName[flowId.Name];
        DebugLog?.LogDebug("Start: `{FlowId}`", flowId);
        return await ResumeRetryPolicy.Run(async ct => {
            using var _ = await _resumeLocks.Lock(flowId, ct).ConfigureAwait(false);
            var cFlowData = await Computed.Capture(() => TryGetData(flowId, ct), ct).ConfigureAwait(false);
            var flowData = cFlowData.Value;
            var existingVersion = flowData?.Version ?? 0L;
            if (flowData is { DeserializationError: null } && !VersionChecker.IsExpected(existingVersion, expectedVersion))
                return flowData;

            var flow = (Flow)flowType.CreateInstance();
            var legacyFlowImpl = flow as ILegacyFlowImpl;
            var flowImpl = (IFlowImpl)flow;
            var console = new FlowConsole();
            if (legacyFlowImpl is not null)
                legacyFlowImpl.SetProperties(flowId, 0, LegacyFlowSteps.Starting, null, console, null);
            else
                flowImpl.SetProperties(flowId, 0, null, console);

            do {
                var storeCommand = new Flows_Store(flow.Id, existingVersion) {
                    Flow = flow,
                };
                existingVersion = await Commander.Call(storeCommand, ct).ConfigureAwait(false);
            }
            while (existingVersion == 0L); // 0L means the flow is removed, but we need it to be started

            using (Computed.BeginIsolation()) // Just in case
                cFlowData = await cFlowData
                    .When(x => x is not null && x.Version >= existingVersion, ct)
                    .ConfigureAwait(false);
            return cFlowData.Value!;
        }, new RetryLogger(Log), cancellationToken).ConfigureAwait(false);
    }

    // The `long` it returns is DbFlow/FlowData.Version
    // [CommandHandler]
    public virtual async Task<long> OnEvent(IFlowEvent command, CancellationToken cancellationToken)
    {
        var flowId = command.FlowId;
        var flowType = Registry.TypeByName[flowId.Name];
        DebugLog?.LogDebug("OnEvent: `{FlowId}` <- {Event}", flowId, command);
        if (flowType.IsAssignableTo(typeof(LegacyFlow))) // The Host handles legacy flows
            return await Host.ProcessEvent(flowId, command, cancellationToken).ConfigureAwait(false);

        if (command is not FlowResume flowResume)
            throw StandardError.Internal($"Unsupported event type: {command.GetType()}.");

        return await ResumeRetryPolicy.Run(async ct => {
            using var _ = await _resumeLocks.Lock(flowId, ct).ConfigureAwait(false);
            Flow originalFlow;
            using (Computed.BeginIsolation()) // Not needed inside a command handler, but let's be safe
                originalFlow = await this.Get(flowId, ct).ConfigureAwait(false);
            if (originalFlow.UntypedResult is not null)
                return originalFlow.Version; // The flow has already completed, so all subsequent events are ignored

            IFlowImpl flow;
            if (flowResume.MustRestart) {
                var console = new FlowConsole(originalFlow.Console.Prefix);
                console.LogSection("[0>]");
                flow = (Flow)flowType.CreateInstance();
                flow.SetProperties(flowId, originalFlow.Version, null, console);
            }
            else {
                flow = originalFlow.Clone();
                flow.Console.LogSection("[>]");
            }

            // Run the HandleResume method
            await flow.OnResume(Services, ct).ConfigureAwait(false);
            return flow.Version;
        }, new RetryLogger(Log), cancellationToken).ConfigureAwait(false);
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
        var flowType = Registry.TypeByName[flowId.Name];
        if (flow?.GetType() == typeof(Flow))
            throw StandardError.Internal("Flow.GetType() == typeof(Flow), i.e., the command is routed to another host.");
        var legacyFlow = flow as LegacyFlow;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(true);

        await dbContext.Set<DbFlow>().Lock(flowId, cancellationToken).ConfigureAwait(false);
        var dbFlow = await dbContext.Set<DbFlow>()
            .FirstOrDefaultAsync(x => Equals(x.Id, flowId.Value), cancellationToken)
            .ConfigureAwait(false);

        var isLegacyRemoval = legacyFlow?.Step == LegacyFlowSteps.Removed;
        if (flow is not null && !isLegacyRemoval) {
            if (dbFlow is null) { // Create
                if (!VersionChecker.IsExpected(0L, expectedVersion))
                    return 0L;

                dbFlow = new DbFlow(flow);
                dbFlow.Version = VersionGenerator.NextVersion(dbFlow.Version);
                dbContext.Add(dbFlow);

                // Any new flow requires a resume or start event
                if (legacyFlow is not null) {
                    if (legacyFlow.Step != LegacyFlowSteps.Starting)
                        throw StandardError.Internal("LegacyFlow.Step should be 'Starting' for a new LegacyFlow.");

                    context.Operation.AddEvent(new LegacyFlowStartEvent(flowId));
                }
                else
                    context.Operation.AddEvent(new FlowResume(flowId));
            }
            else { // Update
                if (!VersionChecker.IsExpected(dbFlow.Version, expectedVersion))
                    return dbFlow.Version;

                dbFlow.UpdateFrom(flow);
                dbFlow.Version = VersionGenerator.NextVersion(dbFlow.Version);
            }
        }
        else {
            // Remove
            if (dbFlow is null) {
                // Nothing to remove, but maybe a version check is needed?
                if (!VersionChecker.IsExpected(0L, expectedVersion))
                    return 0L;
            }
            else {
                if (!VersionChecker.IsExpected(dbFlow.Version, expectedVersion))
                    return dbFlow.Version;

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
            _cache[flowId] = dbFlow?.ToFlowData(flowType, flowId);
            // Invalidate TryGetData cache
            using (Invalidation.Begin())
                _ = TryGetData(flowId, default);
            return Task.CompletedTask;
        });

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbFlow?.Version ?? 0L;
    }
}
