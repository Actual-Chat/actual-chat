using ActualChat.Flows.Db;
using ActualChat.Flows.Infrastructure;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Interception;
using ActualLab.IO;
using ActualLab.Resilience;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Flows;

public class DbFlows(IServiceProvider services) : DbServiceBase<FlowsDbContext>(services), IFlows
{
    protected FlowRegistry Registry { get; } = services.GetRequiredService<FlowRegistry>();
    protected FlowHost Host { get; } = services.GetRequiredService<FlowHost>();
    protected IDbEntityResolver<string, DbFlow> EntityResolver { get; } = services.DbEntityResolver<string, DbFlow>();
    protected IByteSerializer Serializer { get; init; } = TypeDecoratingByteSerializer.Default;

    public IRetryPolicy GetOrStartRetryPolicy { get; init; } = new RetryPolicy(3, RetryDelaySeq.Exp(0.25, 1));

    // [ComputeMethod]
    public virtual async Task<FlowData> GetData(FlowId flowId, CancellationToken cancellationToken = default)
    {
        var dbFlow = await EntityResolver.Get(flowId, cancellationToken).ConfigureAwait(false);
        return dbFlow == null ? default
            : new(dbFlow.Version, dbFlow.Step, dbFlow.Data);
    }

    // [ComputeMethod]
    public virtual Task<Flow?> Get(FlowId flowId, CancellationToken cancellationToken = default)
        => Read(flowId, cancellationToken);

    // Regular method!
    public virtual Task<Flow> GetOrStart(FlowId flowId, CancellationToken cancellationToken = default)
    {
        var flowType = Registry.Types[flowId.Name];
        Flow.RequireCorrectType(flowType);

        var retryLogger = new RetryLogger(Log);
        return GetOrStartRetryPolicy.RunIsolated(async ct => {
            var flow = await Get(flowId, ct).ConfigureAwait(false);
            if (flow != null)
                return flow;

            flow = (Flow)flowType.CreateInstance();
            flow.Initialize(flowId, 0, false, default);
            var storeCommand = new Flows_Store(flow.Id, 0) { Flow = flow };
            var version = await Commander.Call(storeCommand, true, ct).ConfigureAwait(false);
            flow.Initialize(flowId, version, false, default);
            return flow;
        }, retryLogger, cancellationToken);
    }

    // The `long` it returns is DbFlow/FlowData.Version
    [ProxyIgnore] // Regular method!
    public virtual Task<long> OnEvent(FlowId flowId, IFlowEvent evt, CancellationToken cancellationToken = default)
        => Host.HandleEvent(flowId, evt, cancellationToken);

    // The `long` it returns is DbFlow/FlowData.Version
    // [CommandHandler]
    public virtual async Task<long> OnStore(Flows_Store command, CancellationToken cancellationToken = default)
    {
        var (flowId, expectedVersion) = command;
        if (Invalidation.IsActive) {
            _ = GetData(flowId, default);
            _ = Get(flowId, default);
            return default;
        }

        flowId.Require();
        var flow = command.Flow.Require();
        var context = CommandContext.GetCurrent();

        var shard = DbHub.ShardResolver.Resolve(flowId);
        var dbContext = await DbHub.CreateCommandDbContext(shard, cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(true);

        var dbFlow = await dbContext.Set<DbFlow>().ForUpdate()
            .FirstOrDefaultAsync(x => Equals(x.Id, flowId.Value), cancellationToken)
            .ConfigureAwait(false);
        var dbFlowExists = dbFlow != null;
        var flowExists = flow.Step == FlowSteps.Removed;
        if (dbFlowExists || flowExists) {
            if (!VersionChecker.IsExpected(dbFlow?.Version ?? 0, expectedVersion))
                VersionChecker.RequireExpected(dbFlow?.Version ?? 0, expectedVersion);
        }

        long version = 0;
        switch (dbFlowExists, flowExists) {
        case (false, false): // Removed -> Removed
            break;
        case (false, true): // Create
            version = VersionGenerator.NextVersion();
            dbContext.Add(new DbFlow() {
                Id = flowId,
                Version = version,
                CanResume = flow.CanResume,
                Step = flow.Step,
                Data = Serialize(flow),
            });
            if (flow.CanResume || flow.Step.IsEmpty)
                context.Operation.AddEvent(new FlowResumeEvent(flowId, true));
            break;
        case (true, false):  // Remove
            dbContext.Remove(dbFlow!);
            break;
        case (true, true):  // Update
            version = VersionGenerator.NextVersion(dbFlow!.Version);
            dbFlow.Version = version;
            dbFlow.CanResume = flow.CanResume;
            dbFlow.Step = flow.Step;
            dbFlow.Data = Serialize(flow);
            break;
        }
        foreach (var e in command.AddEvents ?? [])
            context.Operation.AddEvent(e);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return version;
    }

    // Protected methods

    protected async Task<Flow?> Read(FlowId flowId, CancellationToken cancellationToken = default)
    {
        var dbFlow = await EntityResolver.Get(flowId, cancellationToken).ConfigureAwait(false);
        var flow = Deserialize(dbFlow?.Data);
        if (flow == null)
            return null;

        flow.Initialize(flowId, dbFlow!.Version, dbFlow.CanResume, dbFlow.Step);
        return flow;
    }

    protected byte[]? Serialize(Flow? flow)
    {
        if (ReferenceEquals(flow, null))
            return null;

        using var buffer = new ArrayPoolBuffer<byte>(256);
        Serializer.Write(buffer, flow, flow.GetType());
        return buffer.WrittenSpan.ToArray();
    }

    protected Flow? Deserialize(byte[]? data)
        => data == null || data.Length == 0
            ? null
            : Serializer.Read<Flow?>(data);
}
