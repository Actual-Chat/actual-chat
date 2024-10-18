namespace ActualChat.UI.Blazor.Components;

public sealed record StepState
{
    public static StepState None { get; } = new StepState();
}

public abstract class Step : Step<StepState>
{
    protected override Task<StepState> ComputeState(CancellationToken cancellationToken)
        => Task.FromResult(StepState.None);
}

public abstract class Step<TState> : ComputedStateComponent<TState>, IStep where TState : class
{
    public virtual bool CanSkip => false;
    public virtual bool IsCompleted => false;
    public virtual string SkipTitle => "Skip";
    public virtual string NextTitle => "Next";
    public IStep? CurrentStep => Stepper.CurrentStep;

    [CascadingParameter] public Stepper Stepper { get; set; } = null!;

    protected override void OnInitialized() {
        if (IsCompleted) {
            MarkCompleted();
            return;
        }

        Stepper.AddStep(this);
    }

    protected abstract Task<bool> Validate();
    protected abstract Task<bool> Save();

    protected virtual void MarkCompleted()
    { }

    protected virtual ValueTask OnSkip()
        => ValueTask.CompletedTask;

    public async ValueTask<bool> TryComplete() {
        if (IsCompleted)
            return true;

        var isValid = await Validate();
        if (!isValid)
            return false;

        var isSaved = await Save();
        if (!isSaved)
            return false;

        MarkCompleted();
        return true;
    }

    public async ValueTask Skip() {
        if (IsCompleted)
            return;

        await OnSkip();
        MarkCompleted();
    }
}
