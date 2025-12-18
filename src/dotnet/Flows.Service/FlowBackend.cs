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

public class FlowBackend : ShardedDbServiceBase<FlowsDbContext>, IFlowBackend
{
    private readonly AsyncLockSet<FlowId> _resumeLocks = new();
    private readonly ILruCache<FlowId, IFlowData?> _cache = new ConcurrentLruCache<FlowId, IFlowData?>(1024);

    // Services
    private FlowHub Hub => field ??= Services.FlowHub();
    private IDbEntityResolver<string, DbFlow> EntityResolver { get; }
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Flows);

    // Properties
    private IRetryPolicy ResumeRetryPolicy { get; init; } = new RetryPolicy(10, RetryDelaySeq.Exp(0.25, 3)) {
        // RpcRerouteException is a special case: it's thrown when the shard is not owned by the current node
        RetryOn = (e, transiency) => e is not RpcRerouteException && transiency is not Transiency.Terminal,
    };

    public FlowBackend(IServiceProvider services) : base(services)
    {
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
        var flowDef = Hub.Defs.ByName[flowId.Name];

        // Check the in-memory cache first
        if (_cache.TryGetValue(flowId, out var flowData))
            return flowData;

        // Read the ground truth
        var dbFlow = await EntityResolver.Get(flowId.Value, cancellationToken).ConfigureAwait(false);
        flowData = dbFlow?.ToFlowData(flowDef.Type, flowId);
        return _cache[flowId] = flowData;  // Update the in-memory cache
    }

    // Regular RPC method!
    public virtual async Task<IFlowData> Start(FlowId flowId, long? expectedVersion, CancellationToken cancellationToken)
    {
        var flowDef = Hub.Defs.ByName[flowId.Name];
        DebugLog?.LogDebug("Start: `{FlowId}`", flowId);
        return await ResumeRetryPolicy.Run(async ct => {
            using var _ = await _resumeLocks.Lock(flowId, ct).ConfigureAwait(false);
            var cFlowData = await Computed.Capture(() => TryGetData(flowId, ct), ct).ConfigureAwait(false);
            var flowData = cFlowData.Value;
            var existingVersion = flowData?.Version ?? 0L;
            if (flowData is not null && !VersionChecker.IsExpected(existingVersion, expectedVersion))
                return flowData;

            var flow = (IFlowImpl)flowDef.Type.CreateInstance();
            var console = new FlowConsole(flow);
            flow.SetProperties(flowId, 0, 0, null, console);
            do {
                var storeCommand = new Flows_Store(flow.Id, existingVersion) {
                    Flow = (Flow)flow,
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
    public virtual async Task<long> OnResume(FlowResumeEvent resumeEvent, CancellationToken cancellationToken)
    {
        var flowId = resumeEvent.FlowId;
        var flowDef = Hub.Defs.ByName[flowId.Name];
        DebugLog?.LogDebug("OnResume: `{FlowId}` <- {Event}", flowId, resumeEvent);

        return await ResumeRetryPolicy.Run(async ct => {
            using var _ = await _resumeLocks.Lock(flowId, ct).ConfigureAwait(false);
            Flow originalFlow;
            using (Computed.BeginIsolation()) // Not needed inside a command handler, but let's be safe
                originalFlow = await Hub.Get(flowId, ct).ConfigureAwait(false);
            if (originalFlow.UntypedResult is not null && !resumeEvent.MustReset)
                return originalFlow.Version; // The flow has already completed, so all subsequent events are ignored

            IFlowImpl flow;
            if (resumeEvent.MustReset) {
                flow = (IFlowImpl)flowDef.Type.CreateInstance();
                var console = new FlowConsole(flow, originalFlow.Console.Prefix);
                flow.SetProperties(flowId, originalFlow.Version, 0, null, console);
            }
            else
                flow = originalFlow.Clone();

            // Run the HandleResume method
            await flow.HandleResume(Hub, resumeEvent.MustReset, ct).ConfigureAwait(false);
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
        var flowDef = Hub.Defs.ByName[flowId.Name];
        if (flow?.GetType() == typeof(Flow))
            throw StandardError.Internal("Flow.GetType() == typeof(Flow), i.e., the command is routed to another host.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(true);

        await dbContext.Set<DbFlow>().Lock(flowId, cancellationToken).ConfigureAwait(false);
        var dbFlow = await dbContext.Set<DbFlow>()
            .FirstOrDefaultAsync(x => Equals(x.Id, flowId.Value), cancellationToken)
            .ConfigureAwait(false);

        if (flow is not null) {
            if (dbFlow is null) { // Create
                if (!VersionChecker.IsExpected(0L, expectedVersion))
                    return 0L;

                dbFlow = new DbFlow(flow);
                dbFlow.Version = VersionGenerator.NextVersion(dbFlow.Version);
                dbContext.Add(dbFlow);

                // Any new flow requires a resume event
                context.Operation.AddEvent(new FlowResumeEvent(flowId));
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
        if (command.Events is { } events)
            foreach (var e in events)
                context.Operation.AddEvent(e);

        // We don't store DbOperation entries for flow updates,
        // coz invalidation must be handled by the local node only.
        // See AddCompletionHandler below.
        context.Operation.MustStore(false);
        context.Operation.AddCompletionHandler(scope => {
            // Update cache to avoid the DB hit in TryGet
            _cache[flowId] = dbFlow?.ToFlowData(flowDef.Type, flowId);
            // Invalidate TryGetData cache
            using (Invalidation.Begin())
                _ = TryGetData(flowId, default);
            return Task.CompletedTask;
        });

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbFlow?.Version ?? 0L;
    }

    // [CommandHandler]
    public virtual async Task OnScheduleResume(Flows_ScheduleResume command, CancellationToken cancellationToken)
    {
        // NOTE(AY): this command handler:
        // - Is guaranteed to always run locally (see `IHasNodeRef` in Flows_Store).
        // - Doesn't run in the invalidation mode (it's an `IDelegatingCommand`).
        // Nevertheless, it has the invalidation logic - see the `AddCompletionHandler` call below.

        var context = CommandContext.GetCurrent();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        context.Operation.AddEvent(command.Item);
        if (command.Items is { } items)
            foreach (var item in items)
                context.Operation.AddEvent(item);

        context.Operation.MustStore(false);
    }
}
