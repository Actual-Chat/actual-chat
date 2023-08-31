namespace ActualChat.UI.Blazor.Components;

public class DiveInModalPageContext
{
    private string? _title;
    private readonly IDiveInModalContext _modalContext;

    public string? Title {
        get => _title;
        set {
            if (OrdinalEquals(_title, value))
                return;
            _title = value;
            StateHasChanged();
        }
    }

    public DiveInModalPageContext(IDiveInModalContext modalContext, string? title)
    {
        _modalContext = modalContext;
        _title = title;
    }

    public void StepIn(string pageId)
        => _modalContext.StepIn(pageId);

    public void Back()
        => _modalContext.Back();

    public void Close()
        => _modalContext.Close();

    public void StateHasChanged()
        => _modalContext.StateHasChanged();
}
