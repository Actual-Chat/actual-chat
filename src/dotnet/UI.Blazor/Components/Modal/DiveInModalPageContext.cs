namespace ActualChat.UI.Blazor.Components;

#pragma warning disable CA1721

public class DiveInModalPageContext
{
    private readonly IDiveInModalContext _modalContext;
    private readonly DiveInDialogPage _page;
    private string _title = "";
    private string _class = "";
    private DialogButtonInfo[] _buttons = [];

    public object? Model => _page.Model;
    public IDictionary<string, object> PageDataBag { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
    public IDictionary<string, object> ModalDataBag => _modalContext.DataBag;

    public string Title {
        get => _title;
        set {
            if (OrdinalEquals(Title, value))
                return;

            _title = value ?? throw new ArgumentOutOfRangeException(nameof(value));
            StateHasChanged();
        }
    }

    public string Class {
        get => _class;
        set {
            if (OrdinalEquals(_class, value))
                return;

            _class = value ?? throw new ArgumentOutOfRangeException(nameof(value));
            StateHasChanged();
        }
    }

    public DialogButtonInfo[] Buttons {
        get => _buttons;
        set {
            _buttons = value ?? throw new ArgumentOutOfRangeException(nameof(value));
            StateHasChanged();
        }
    }

    // ReSharper disable once ConvertToPrimaryConstructor
    public DiveInModalPageContext(IDiveInModalContext modalContext, DiveInDialogPage page)
    {
        _modalContext = modalContext;
        _page = page;
    }

    public T GetModel<T>()
        => (T)Model!;

    public void Close()
        => _modalContext.Close();

    public void StepIn(DiveInDialogPage page)
        => _modalContext.StepIn(page);

    private void StateHasChanged()
        => _modalContext.StateHasChanged();
}
