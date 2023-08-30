namespace ActualChat.UI.Blazor.Components;

public interface IDiveInModalContext
{
    void StepIn(string pageId);

    void Back();

    void Close();

    void StateHasChanged();
}
