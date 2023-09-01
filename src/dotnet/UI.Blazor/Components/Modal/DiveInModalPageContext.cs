namespace ActualChat.UI.Blazor.Components;

public class DiveInModalPageContext
{
    private readonly IDiveInModalContext _modalContext;
    private string? _title;
    private DialogFrameNarrowViewSettings? _narrowViewSettings;

    public string? Title {
        get => _title;
        set {
            if (OrdinalEquals(_title, value))
                return;
            _title = value;
            StateHasChanged();
        }
    }

    public DialogFrameNarrowViewSettings? NarrowViewSettings {
        get => _narrowViewSettings;
        set {
            if (_narrowViewSettings == value)
                return;
            _narrowViewSettings = value;
            StateHasChanged();
        }
    }

    public DataBag PageDataBag { get; } = new ();
    public DataBag ModalDataBag => _modalContext.DataBag;

    public DiveInModalPageContext(
        IDiveInModalContext modalContext,
        string? title,
        DialogFrameNarrowViewSettings? narrowViewSettings)
    {
        _modalContext = modalContext;
        _title = title;
        _narrowViewSettings = narrowViewSettings;
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
