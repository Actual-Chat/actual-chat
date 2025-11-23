using ActualChat.Flows.Infrastructure;

namespace ActualChat.Flows;

internal class MasterFlowStarter(IServiceProvider services) : LegacyShardWorker(services, ShardScheme.FlowsBackend)
{
    private readonly ConcurrentDictionary<Type, Unit> _flowTypesToStart = new();

    [field: AllowNull, MaybeNull]
    private FlowRegistry FlowRegistry => field ??= Services.GetRequiredService<FlowRegistry>();
    [field: AllowNull, MaybeNull]
    private IFlows Flows => field ??= Services.GetRequiredService<IFlows>();
    [field: AllowNull, MaybeNull]
    private ShardKeyResolver<FlowId> FlowIdShardKeyResolver => field ??= ShardKeyResolvers.Get<FlowId>();

    protected override Task OnStart(CancellationToken cancellationToken)
    {
        if (!FlowRegistry.UseMasterFlows)
            return Task.CompletedTask;

        var masterFlowTypes = FlowRegistry.NameByType.Keys.Where(x => x.IsAssignableTo(typeof(IMasterFlow)));
        foreach (var masterFlowType in masterFlowTypes)
            _flowTypesToStart[masterFlowType] = default;
        return Task.CompletedTask;
    }

    protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        foreach (var flowType in _flowTypesToStart.Keys) {
            var flowId = GetFlowId(flowType);
            var requiredShardIndex = ShardScheme.GetShardIndex(FlowIdShardKeyResolver.Invoke(flowId));
            if (shardIndex != requiredShardIndex)
                continue;

            var task = StartMasterFlow(flowType, cancellationToken);
            tasks.Add(task);
        }
        if (tasks.Count > 0)
            await Task.WhenAll(tasks).ConfigureAwait(false);
        else
            await TaskExt.NeverEnding(cancellationToken).ConfigureAwait(false);
    }

    private async Task StartMasterFlow(Type flowType, CancellationToken cancellationToken)
    {
        await Flows
            .Reset(GetFlowId(flowType), null, nameof(MasterFlowStarter), cancellationToken)
            .ConfigureAwait(false);
        _flowTypesToStart.TryRemove(flowType, out _);
    }

    private FlowId GetFlowId(Type masterFlowType)
        => FlowRegistry.NewId(masterFlowType, "");
}
