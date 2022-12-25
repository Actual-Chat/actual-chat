namespace ActualChat.UI.Blazor.Components;

public abstract class Step : ComponentBase
{
    public bool IsCompleted { get; private set; }
    public Step? CurrentStep => Stepper.CurrentStep;

    [CascadingParameter] public Stepper Stepper { get; set; } = null!;

    protected override void OnInitialized()
        => Stepper.AddStep(this);

    protected abstract Task<bool> Validate();

    protected abstract Task Save();

    public void Refresh()
        => StateHasChanged();

    public async Task<bool> Complete()
    {
        if (IsCompleted)
            return true;
        var isValid = await Validate();
        if (!isValid)
            return false;
        await Save();
        IsCompleted = true;
        return true;
    }
}
