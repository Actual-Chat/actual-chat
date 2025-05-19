namespace ActualChat.UI.Blazor.Components;

public interface IDiveInModalContext
{
    public MutablePropertyBag Items { get; }

    void StepIn(DiveInDialogPage pageDescriptor);
    void Close();
    void StateHasChanged();
}
