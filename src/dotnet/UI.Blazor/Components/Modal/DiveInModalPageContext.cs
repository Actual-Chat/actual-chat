namespace ActualChat.UI.Blazor.Components;

#pragma warning disable CA1721

public class DiveInModalPageContext
{
    private readonly IDiveInModalContext _modalContext;
    private readonly DiveInDialogPage _page;

    public object? Model => _page.Model;
    public MutablePropertyBag Items { get; } = new();
    public MutablePropertyBag ContextItems => _modalContext.Items;

    public string Title {
        get;
        set {
            if (OrdinalEquals(Title, value))
                return;

            field = value ?? throw new ArgumentOutOfRangeException(nameof(value));
            StateHasChanged();
        }
    } = "";

    public string Class {
        get;
        set {
            if (OrdinalEquals(field, value))
                return;

            field = value ?? throw new ArgumentOutOfRangeException(nameof(value));
            StateHasChanged();
        }
    } = "";

    public DialogButtonInfo[] Buttons {
        get;
        set {
            field = value ?? throw new ArgumentOutOfRangeException(nameof(value));
            StateHasChanged();
        }
    } = [];

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
