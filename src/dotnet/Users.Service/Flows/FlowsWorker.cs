using ActualChat.Flows;

namespace ActualChat.Users.Flows;

internal class FlowsWorker(IFlows flows) : WorkerBase
{
    protected override async Task OnRun(CancellationToken cancellationToken)
        => await flows.GetOrStart<MasterFlow>("", cancellationToken).ConfigureAwait(false);
}
