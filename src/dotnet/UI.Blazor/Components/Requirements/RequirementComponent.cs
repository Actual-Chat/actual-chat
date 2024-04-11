using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.Components;

public abstract class RequirementComponent : ComputedStateComponent<Unit>
{
    [CascadingParameter] protected RequirementChecker? RequirementChecker { get; private set; }

    public abstract Task<Unit> Require(CancellationToken cancellationToken);

    protected override ComputedState<Unit>.Options GetStateOptions()
        => new() {
            UpdateDelayer = FixedDelayer.Zero,
            Category = GetStateCategory(),
        };

    protected sealed override Task<Unit> ComputeState(CancellationToken cancellationToken)
        => Require(cancellationToken);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (State.HasError)
            RequirementChecker?.Add(this);
    }
}
