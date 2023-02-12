namespace ActualChat.UI.Blazor.Components;

public abstract class Step : ComponentBase
{
    public abstract bool IsCompleted { get; }
    public Step? CurrentStep => Stepper.CurrentStep;

    [CascadingParameter] public Stepper Stepper { get; set; } = null!;

    protected override void OnInitialized()
    {
        if (IsCompleted)
            return;

        Stepper.AddStep(this);
    }

    protected abstract Task<bool> Validate();
    protected abstract Task Save();
    protected abstract void MarkCompleted();

    public async ValueTask<bool> TryComplete()
    {
        if (IsCompleted)
            return true;

        var isValid = await Validate();
        if (!isValid)
            return false;

        await Save();
        MarkCompleted();
        return true;
    }
}
