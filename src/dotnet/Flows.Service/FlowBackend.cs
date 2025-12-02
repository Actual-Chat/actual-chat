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
    private IRetryPolicy ClearCacheRetryPolicy { get; init; } = new RetryPolicy(RetryDelaySeq.Fixed(0.1));

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

    // [ComputeMethod]
    public virtual async Task<IFlowData?> TryGetData(FlowId flowId, CancellationToken cancellationToken)
    {
        var flowType = Registry.TypeByName[flowId.Name];

        // Check cache
        if (false && _cache.TryGetValue(flowId, out var flowData))
            return flowData;

        // Read the ground truth
        var dbFlow = await EntityResolver.Get(flowId.Value, cancellationToken).ConfigureAwait(false);
        flowData = dbFlow?.ToFlowData(flowType, flowId);
        return _cache[flowId] = flowData;  // Update cache
    }

    // Regular RPC method!
    public virtual async Task<Flow> Start(FlowId flowId, CancellationToken cancellationToken)
    {
        var flowType = Registry.TypeByName[flowId.Name];
        DebugLog?.LogDebug("Start: `{FlowId}`", flowId);
        return await ResumeRetryPolicy.Run(async ct => {
            using var _ = await _resumeLocks.Lock(flowId, ct).ConfigureAwait(false);
            var version = 0L;
            while (true) {
                IFlowData? flowData;
                using (Computed.BeginIsolation()) {
                    flowData = await TryGetData(flowId, ct).ConfigureAwait(false);
                }
                if (flowData is not null && flowData.Version >= version)
                    return flowData.Flow;

                if (version > 0L) {
                    // We already executed Flows_Store command and it succeeded
                    await TickSource.Default.WhenNextTick().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                var flow = (Flow)flowType.CreateInstance();
                var legacyFlowImpl = flow as ILegacyFlowImpl;
                var flowImpl = (IFlowImpl)flow;
                var console = new FlowConsole();
                if (legacyFlowImpl is not null)
                    legacyFlowImpl.SetProperties(flowId, 0, LegacyFlowSteps.Starting, null, console, null);
                else
                    flowImpl.SetProperties(flowId, 0, null, console);

                // Flow_Store is guaranteed to run locally
                var storeCommand = new Flows_Store(flow.Id, 0) {
                    Flow = flow,
                };
                version = await Commander.Call(storeCommand, ct).ConfigureAwait(false);
            }
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
                VersionChecker.RequireExpected(dbFlow.Version, expectedVersion);
                dbFlow.UpdateFrom(flow);
                dbFlow.Version = VersionGenerator.NextVersion(dbFlow.Version);
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
            _cache[flowId] = dbFlow?.ToFlowData(flowType, flowId);
            // Invalidate TryGet cache
            using (Invalidation.Begin())
                _ = TryGetData(flowId, default);
            return Task.CompletedTask;
        });

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbFlow?.Version ?? 0L;
    }
}
