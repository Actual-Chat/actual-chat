namespace ActualChat.UI.Blazor.Components;

public interface IStep
{
    public bool CanSkip { get; }
    public bool IsCompleted { get; }
    public string SkipTitle { get; }
    public string NextTitle { get; }
    public IStep? CurrentStep => Stepper.CurrentStep;
    [CascadingParameter] public Stepper Stepper { get; set; }
    ValueTask<bool> TryComplete();
    ValueTask Skip();

    void NotifyStateHasChanged()
    {
        if (this is not ComponentBase component)
            return;

        component.NotifyStateHasChanged();
    }
}
