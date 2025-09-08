using ActualChat.Flows.Infrastructure;

namespace ActualChat.Flows;

internal class MasterFlowStarter(IServiceProvider services) : LegacyShardWorker(services, ShardScheme.FlowsBackend)
{
    private readonly Dictionary<Type, (FlowId FlowId, int RequiredShardIndex)> _masterFlows = new ();
    private bool _isCompleted;

    [field: AllowNull, MaybeNull]
    private FlowRegistry FlowRegistry => field ??= Services.GetRequiredService<FlowRegistry>();
    [field: AllowNull, MaybeNull]
    private IFlows Flows => field ??= Services.GetRequiredService<IFlows>();

    protected override Task OnStart(CancellationToken cancellationToken)
    {
        var shardKeyResolver = ShardKeyResolvers.Get<FlowId>(typeof(MasterFlowStarter));
        var masterFlowTypes = FlowRegistry.NameByType.Keys.Where(x => x.IsAssignableTo(typeof(IMasterFlow)));
        foreach (var masterFlowType in masterFlowTypes) {
            var flowId = FlowRegistry.NewId(masterFlowType, "");
            var requiredShardIndex = new ShardRef(ShardScheme, shardKeyResolver.Invoke(flowId)).Normalize().Key;
            _masterFlows.Add(masterFlowType, (flowId, requiredShardIndex));
        }

        return Task.CompletedTask;
    }

    protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        if (_masterFlows.Values.All(x => x.RequiredShardIndex != shardIndex) || _isCompleted) {
            using var dTask = cancellationToken.ToTask();
            await dTask.Resource.SilentAwait(false);
            return;
        }

        foreach (var (masterFlowType, (flowId, _)) in _masterFlows)
            await Flows.StartOrReset(masterFlowType,
                    flowId.Arguments,
                    null,
                    nameof(MasterFlowStarter),
                    cancellationToken)
                .ConfigureAwait(false);
        _isCompleted = true;
    }
}
