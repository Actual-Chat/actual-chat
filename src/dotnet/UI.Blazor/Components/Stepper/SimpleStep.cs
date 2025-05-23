namespace ActualChat.UI.Blazor.Components;

public abstract class SimpleStep<THub> : Step<THub, RefUnit>
    where THub : UIHub
{
    protected SimpleStep()
        => MustRenderAfterEvent = true;

    protected override Task<RefUnit> ComputeState(CancellationToken cancellationToken)
        => RefUnit.InstanceTask;
}
