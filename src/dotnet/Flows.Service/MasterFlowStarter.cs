using ActualChat.Flows.Infrastructure;

namespace ActualChat.Flows;

internal class MasterFlowStarter(IServiceProvider services) : LegacyShardWorker(services, ShardScheme.FlowsBackend)
{
    private readonly ConcurrentDictionary<Type, FlowId> _flowsToStart = new();

    [field: AllowNull, MaybeNull]
    private FlowRegistry FlowRegistry => field ??= Services.GetRequiredService<FlowRegistry>();
    [field: AllowNull, MaybeNull]
    private IFlows Flows => field ??= Services.GetRequiredService<IFlows>();
    [field: AllowNull, MaybeNull]
    private ShardKeyResolver<FlowId> FlowIdShardKeyResolver
        => field ??= ShardKeyResolvers.Get<FlowId>(typeof(MasterFlowStarter));

    protected override Task OnStart(CancellationToken cancellationToken)
    {
        var masterFlowTypes = FlowRegistry.NameByType.Keys.Where(x => x.IsAssignableTo(typeof(IMasterFlow)));
        foreach (var masterFlowType in masterFlowTypes) {
            var flowId = FlowRegistry.NewId(masterFlowType, "");
            _flowsToStart[masterFlowType] = flowId;
        }
        return Task.CompletedTask;
    }

    protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        foreach (var (masterFlowType, flowId) in _flowsToStart) {
            var requiredShardIndex = FlowIdShardKeyResolver.Invoke(flowId);
            if (shardIndex != requiredShardIndex)
                continue;

            var task = StartMasterFlow(masterFlowType, flowId, cancellationToken);
            tasks.Add(task);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task StartMasterFlow(Type flowType, FlowId flowId, CancellationToken cancellationToken)
    {
        await Flows
            .StartOrReset(flowType, flowId.Arguments, null, nameof(MasterFlowStarter), cancellationToken)
            .ConfigureAwait(false);
        _flowsToStart.TryRemove(flowType, out _);
    }
}
