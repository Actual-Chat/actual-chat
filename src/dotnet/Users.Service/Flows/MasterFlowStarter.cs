using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Queues;

namespace ActualChat.Users.Flows;

internal class MasterFlowStarter(IServiceProvider services)
    : ShardWorker(services, ShardScheme.FlowsBackend)
{
    private FlowRegistry FlowRegistry { get; } = services.GetRequiredService<FlowRegistry>();
    private IFlows Flows { get; } = services.GetRequiredService<IFlows>();
    private IQueues Queues { get; } = services.Queues();

    private readonly Dictionary<Type, (FlowId FlowId, int RequiredShardIndex)> _masterFlows = new ();
    private bool _isCompleted;

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

        foreach (var (masterFlowType, (flowId, _)) in _masterFlows) {
            var flow = await Flows.Get(flowId, cancellationToken).ConfigureAwait(false);
            if (flow == null)
                await Flows.GetOrStart(masterFlowType, "", cancellationToken).ConfigureAwait(false);
            else {
                var resetEvent = new FlowResetEvent(flowId);
                await Queues.Enqueue(resetEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        _isCompleted = true;
    }
}
