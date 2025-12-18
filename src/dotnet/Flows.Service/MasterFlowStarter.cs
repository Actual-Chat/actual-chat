using ActualChat.Flows.Infrastructure;

namespace ActualChat.Flows;

internal class MasterFlowStarter(IServiceProvider services) : ShardWorker(services, ShardScheme.FlowsBackend)
{
    private readonly ConcurrentDictionary<Type, Unit> _flowTypesToStart = new();

    private FlowHub Hub => field ??= Services.FlowHub();
    private ShardKeyResolver<FlowId> FlowIdShardKeyResolver => field ??= ShardKeyResolvers.Get<FlowId>();

    protected override Task OnStart(CancellationToken cancellationToken)
    {
        if (!Services.GetRequiredService<FlowDefsBuilder>().UseMasterFlows)
            return Task.CompletedTask;

        var masterFlowTypes = Hub.Defs.ByType.Keys.Where(x => x.IsAssignableTo(typeof(IMasterFlow)));
        foreach (var masterFlowType in masterFlowTypes)
            _flowTypesToStart[masterFlowType] = default;
        return Task.CompletedTask;
    }

    protected override async Task OnRun(ShardOwnership shardOwnership, CancellationToken cancellationToken)
    {
        var shardIndex = shardOwnership.ShardIndex;
        var tasks = new List<Task>();
        foreach (var flowType in _flowTypesToStart.Keys) {
            var flowId = Hub.NewId(flowType, "");
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
        var resumeEvent = Hub.NewResumeEvent(flowType, "");
        await resumeEvent.Schedule(cancellationToken).ConfigureAwait(false);
        _flowTypesToStart.TryRemove(flowType, out _);
    }
}
