namespace ActualChat.UI.Blazor.Components;

public interface IDiveInModalContext
{
    public DataBag DataBag { get; }

    void StepIn(string pageId);

    void Back();

    void Close();

    void StateHasChanged();
}
