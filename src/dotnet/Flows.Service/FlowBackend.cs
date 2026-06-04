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

/// <summary>
/// Backend service implementation for managing flow state persistence and resumption.
/// </summary>
public class FlowBackend : ShardedDbServiceBase<FlowsDbContext>, IFlowBackend
{
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromHours(6);
    private const int MaxListLimit = 1000;

    private readonly AsyncLockSet<FlowId> _resumeLocks = new(LockReentryMode.CheckedPass);
    private readonly VersionedComputeMethodPrimer<FlowId, long, IFlowData?> _flowDataPrimer;

    // Services
    private FlowHub FlowHub => field ??= Services.FlowHub();
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
        _flowDataPrimer = new VersionedComputeMethodPrimer<FlowId, long, IFlowData?>(TryGetData);
    }

    // [ComputeMethod]
    public virtual async Task<IFlowData?> TryGetData(FlowId flowId, CancellationToken cancellationToken)
    {
        if (_flowDataPrimer.TryUsePrimed(flowId, out var primed))
            return primed;

        var flowDef = FlowHub.Defs.ByName[flowId.Name];
        var dbFlow = await EntityResolver.Get(flowId.Value, cancellationToken).ConfigureAwait(false);
        return dbFlow?.ToFlowData(flowDef.Type, flowId);
    }

    // Regular RPC method!
    public virtual async Task<IFlowData> Start(FlowId flowId, long? expectedVersion, CancellationToken cancellationToken)
    {
        var flowDef = FlowHub.Defs.ByName[flowId.Name];
        DebugLog?.LogDebug("Start: `{FlowId}`", flowId);
        return await ResumeRetryPolicy.Run(async ct => {
            using var releaser = await _resumeLocks.Lock(flowId, ct).ConfigureAwait(false);
            releaser.MarkLockedLocally();

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

    // Regular RPC method! Pinned to a single node via ShardKeyResolver<FlowsQuery>.
    public virtual async Task<FlowsReport> List(FlowsQuery query, CancellationToken cancellationToken)
    {
        var now = FlowHub.Clocks.SystemClock.Now;
        var cutoffVersion = (now - StuckThreshold).EpochOffset.Ticks;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var aggRows = await dbContext.Database
            .SqlQuery<AggRow>($"""
                SELECT split_part(id, ':', 1) AS "Name",
                       COUNT(*) FILTER (WHERE NOT is_completed AND version >= {cutoffVersion})::int AS "Active",
                       COUNT(*) FILTER (WHERE is_completed AND NOT is_failed)::int AS "Completed",
                       COUNT(*) FILTER (WHERE is_completed AND is_failed)::int AS "Failed",
                       COUNT(*) FILTER (WHERE NOT is_completed AND version < {cutoffVersion})::int AS "Stuck"
                FROM _flows
                GROUP BY split_part(id, ':', 1)
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var aggregates = aggRows
            .Select(a => new FlowTypeStat(a.Name, a.Active, a.Completed, a.Failed, a.Stuck))
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .ToArray();

        var rowsQuery = dbContext.Flows.AsNoTracking();
        if (!query.Name.IsNullOrEmpty()) {
            var namePrefix = query.Name + ":";
            rowsQuery = rowsQuery.Where(f => f.Id.StartsWith(namePrefix));
        }
        if (query.ProblematicOnly)
            rowsQuery = rowsQuery.Where(f =>
                (f.IsCompleted && f.IsFailed) || (!f.IsCompleted && f.Version < cutoffVersion));

        var limit = query.Limit.Clamp(0, MaxListLimit);
        var rawRows = await rowsQuery
            .OrderBy(f => f.Id)
            .Take(limit)
            .Select(f => new { f.Id, f.IsCompleted, f.IsFailed, f.Version })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var rows = rawRows
            .Select(r => new FlowSummary(
                r.Id,
                GetName(r.Id),
                GetStatus(r.IsCompleted, r.IsFailed, r.Version, cutoffVersion),
                r.Version,
                new Moment(TimeSpan.FromTicks(r.Version))))
            .ToArray();

        return new FlowsReport(aggregates, rows, now);
    }

    // The `long` it returns is DbFlow/FlowData.Version
    // [CommandHandler]
    public virtual async Task<long> OnResume(FlowResumeEvent resumeEvent, CancellationToken cancellationToken)
    {
        var flowId = resumeEvent.FlowId;
        var flowDef = FlowHub.Defs.ByName[flowId.Name];
        DebugLog?.LogDebug("OnResume: `{FlowId}` <- {Event}", flowId, resumeEvent);

        return await ResumeRetryPolicy.Run(async ct => {
            using var releaser = await _resumeLocks.Lock(flowId, ct).ConfigureAwait(false);
            releaser.MarkLockedLocally();

            Flow originalFlow;
            using (Computed.BeginIsolation()) // Not needed inside a command handler, but let's be safe
                originalFlow = await FlowHub.Get(flowId, ct).ConfigureAwait(false);
            if (originalFlow.UntypedResult is not null && !resumeEvent.MustReset)
                return originalFlow.Version; // The flow has already completed, so all subsequent events are ignored

            IFlowImpl flow;
            var initReason = resumeEvent.MustReset
                ? "explicit reset"
                : originalFlow.DataVersion < flowDef.DataVersion
                    ? originalFlow.DataVersion == 0
                        ? "new flow"
                        : $"data version mismatch ({originalFlow.DataVersion} < {flowDef.DataVersion})"
                    : null;
            if (initReason is not null) {
                flow = (IFlowImpl)flowDef.Type.CreateInstance();
                var console = new FlowConsole(flow, originalFlow.Console.Prefix);
                flow.SetProperties(flowId, originalFlow.Version, flowDef.DataVersion, null, console);
            }
            else
                flow = originalFlow.Clone();

            // Run the HandleResume method
            using var _ = Computed.BeginIsolation(); // Just in case
            await flow.HandleResume(FlowHub, initReason, ct).ConfigureAwait(false);
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
                context.Operation.AddEvent(FlowHub.NewResumeEvent(flowId));
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
        context.Operation.AddCompletionHandler(async scope => {
            if (scope.IsCommitted != true)
                return;
            if (dbFlow is null)
                return; // We don't prime removed flows

            // Hand off the freshly stored value to TryGetData's next recompute,
            // so the invalidation triggered here doesn't require a follow-up DB read.
            // The version guard protects against out-of-order completion handlers — if a
            // newer store's handler already primed, ours is a no-op (and skips invalidation).
            var flowDef = FlowHub.Defs.ByName[flowId.Name];
            var flowData = dbFlow.ToFlowData(flowDef.Type, flowId);
            var version = dbFlow.Version;
            await _flowDataPrimer.Prime(flowId, version, flowData, CancellationToken.None).ConfigureAwait(false);
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

    // Private methods

    private static string GetName(string flowId)
    {
        var nameLength = flowId.IndexOf(':');
        return nameLength <= 0 ? flowId : flowId[..nameLength];
    }

    private static FlowStatus GetStatus(bool isCompleted, bool isFailed, long version, long cutoffVersion)
        => isCompleted
            ? isFailed ? FlowStatus.Failed : FlowStatus.Completed
            : version < cutoffVersion ? FlowStatus.Stuck : FlowStatus.Active;

    // Nested types

    private sealed class AggRow
    {
        public string Name { get; set; } = "";
        public int Active { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
        public int Stuck { get; set; }
    }
}
