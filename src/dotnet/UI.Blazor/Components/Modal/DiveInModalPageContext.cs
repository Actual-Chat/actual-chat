namespace ActualChat.UI.Blazor.Components;

public class DiveInModalPageContext(IDiveInModalContext modalContext, string? title)
{
    public string? Title { get; set; } = title;

    public void StepIn(string pageId)
        => modalContext.StepIn(pageId);

    public void Back()
        => modalContext.Back();

    public void Close()
        => modalContext.Close();

    public void StateHasChanged()
        => modalContext.StateHasChanged();
}
