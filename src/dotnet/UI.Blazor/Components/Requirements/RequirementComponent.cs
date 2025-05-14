using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.Components;

public abstract class RequirementComponent : ComputedStateComponent<UIHub, object?>
{
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Hub.LogFor(GetType());

    [CascadingParameter] protected RequirementChecker? RequirementChecker { get; private set; }

    public abstract Task Require(CancellationToken cancellationToken);

    protected override ComputedState<object?>.Options GetStateOptions()
        => ComputedStateComponent.GetStateOptions(GetType(),
            static t => new ComputedState<object?>.Options() {
                UpdateDelayer = FixedDelayer.NextTick,
                Category = GetStateCategory(t),
            });

    protected sealed override async Task<object?> ComputeState(CancellationToken cancellationToken)
    {
        await Require(cancellationToken).ConfigureAwait(false);
        return null;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (State.HasError)
            RequirementChecker?.Add(this);
    }
}
