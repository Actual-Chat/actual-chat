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
    private readonly ILruCache<FlowId, Flow?> _cache = new ConcurrentLruCache<FlowId, Flow?>(1024);

    // Services
    private FlowRegistry Registry { get; }
    private FlowHost Host { get; }
    private IDbEntityResolver<string, DbFlow> EntityResolver { get; }
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Flows);

    // Properties
    private IRetryPolicy ResumeRetryPolicy { get; init; } = new RetryPolicy(3, RetryDelaySeq.Exp(0.25, 1)) {
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
    public virtual Task<Flow?> TryGet(FlowId flowId, CancellationToken cancellationToken)
        => TryGetImpl(flowId, cancellationToken);

    // Regular RPC method!
    public virtual async Task<Flow> Start(FlowId flowId, CancellationToken cancellationToken)
    {
        var flowType = Registry.TypeByName[flowId.Name];
        // RunIsolated also ensures the code below doesn't
        // produce any dependencies for the caller, even though it calls TryGet.
        var flow = await TryGet(flowId, cancellationToken).ConfigureAwait(false);
        if (flow is not null)
            return flow;

        // Ensure the shard for this flow is owned by the current node
        var shardOwnership = await ShardOwner.RequireOwnedOrReroute(flowId, cancellationToken).ConfigureAwait(false);
        using var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);
        var linkedToken = linkedCts.Token;

        // Ensure the resume logic below doesn't run concurrently for the same flow
        using var _ = await _resumeLocks.Lock(flowId, linkedToken).ConfigureAwait(false);

        return await ResumeRetryPolicy.Run(async _ => {
            try {
                flow = (Flow)flowType.CreateInstance();
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
                var version = await Commander.Call(storeCommand, linkedToken).ConfigureAwait(false);
                using (Computed.BeginIsolation())
                    flow = await TryGet(flowId, linkedToken).ConfigureAwait(false);
                if (flow.Require().Version < version)
                    throw StandardError.Internal("Something went wrong: Flow.Version is lower than expected.");
                return flow;
            }
            catch (Exception e) when (e.IsCancellationOf(shardOwnership.LockToken)) {
                throw RpcRerouteException.MustReroute(); // Retry policy doesn't retry on this one
            }
        }, new RetryLogger(Log), linkedToken).ConfigureAwait(false);
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
            if (command is not FlowResume flowResume)
                throw StandardError.Internal($"Unsupported event type: {command.GetType()}.");

            // Ensure the shard for this flow is owned by the current node
            var shardOwnership = await ShardOwner.RequireOwnedOrReroute(flowId, cancellationToken).ConfigureAwait(false);
            using var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);
            var linkedToken = linkedCts.Token;

            // Ensure the resume logic below doesn't run concurrently for the same flow
            using var _ = await _resumeLocks.Lock(flowId, linkedToken).ConfigureAwait(false);

            // Get the flow, clone it
            var originalFlow = await this.Get(flowId, linkedToken).ConfigureAwait(false);
            if (originalFlow.UntypedResult is not null)
                return originalFlow.Version; // The flow has already completed, so all subsequent events are ignored

            return await ResumeRetryPolicy.Run(async _ => {
                try {
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
                    await flow.OnResume(Services, linkedToken).ConfigureAwait(false);
                    return flow.Version;
                }
                catch (Exception e) when (e.IsCancellationOf(shardOwnership.LockToken)) {
                    throw RpcRerouteException.MustReroute(); // Retry policy doesn't retry on this one
                }
            }, new RetryLogger(Log), linkedToken).ConfigureAwait(false);
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
            _cache[flowId] = dbFlow?.ToModel(flowId);
            // Invalidate TryGet cache
            using (Invalidation.Begin())
                _ = TryGet(flowId, default);
            return Task.CompletedTask;
        });

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
        flow = dbFlow?.ToModel(flowId);
        return _cache[flowId] = flow;  // Update cache
    }
}
