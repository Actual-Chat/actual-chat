namespace ActualChat.UI.Blazor.Components;

public interface IDiveInModalContext
{
    public IDictionary<string, object> DataBag { get; }

    void StepIn(DiveInDialogPage pageDescriptor);

    void Close();

    void StateHasChanged();
}
