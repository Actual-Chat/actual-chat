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

    private FlowId _flowId;
    private int _requiredShardIndex;
    private bool _isCompleted;

    protected override Task OnStart(CancellationToken cancellationToken)
    {
        _flowId = FlowRegistry.NewId<MasterFlow>("");
        var shardKeyResolver = ShardKeyResolvers.Get<FlowId>(typeof(MasterFlowStarter));
        _requiredShardIndex = new ShardRef(ShardScheme, shardKeyResolver.Invoke(_flowId)).Normalize().Key;
        return Task.CompletedTask;
    }

    protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        if (shardIndex != _requiredShardIndex || _isCompleted) {
            using var dTask = cancellationToken.ToTask();
            await dTask.Resource.SilentAwait(false);
            return;
        }

        var flow = await Flows.Get(_flowId, cancellationToken).ConfigureAwait(false);
        if (flow == null)
            await Flows.GetOrStart<MasterFlow>("", cancellationToken).ConfigureAwait(false);
        else {
            var resetEvent = new FlowResetEvent(_flowId);
            await Queues.Enqueue(resetEvent, cancellationToken).ConfigureAwait(false);
        }
        _isCompleted = true;
    }
}
