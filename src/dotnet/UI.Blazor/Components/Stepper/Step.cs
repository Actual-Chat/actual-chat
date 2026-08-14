using ActualChat.UI.Blazor.Resources;

namespace ActualChat.UI.Blazor.Components;

public abstract class Step<THub, TModel>
    : ComputedStateComponent<THub, TModel>, IStep
    where THub : UIHub
    where TModel : class
{
    public virtual bool CanSkip => false;
    public virtual bool IsCompleted => false;
    public virtual string SkipTitle => L.Common_Skip;
    public virtual string NextTitle => L.Common_Next;
    public IStep? CurrentStep => Stepper.CurrentStep;

    [CascadingParameter] public Stepper Stepper { get; set; } = null!;

    protected override void OnInitialized() {
        Stepper.AddStep(this);
        if (IsCompleted)
            MarkCompleted();
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
