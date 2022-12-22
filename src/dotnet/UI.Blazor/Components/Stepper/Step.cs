namespace ActualChat.UI.Blazor.Components;

public abstract class Step : ComponentBase
{
    [CascadingParameter] public Stepper Stepper { get; set; } = null!;

    protected override void OnInitialized()
        => Stepper.AddStep(this);

    public void Refresh()
        => StateHasChanged();

    public abstract bool Validate();
}
