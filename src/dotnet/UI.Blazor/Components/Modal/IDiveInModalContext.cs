namespace ActualChat.UI.Blazor.Components;

public interface IDiveInModalContext
{
    public DataBag DataBag { get; }

    void StepIn(DiveInDialogPage pageDescriptor);

    void Close();

    void StateHasChanged();
}
