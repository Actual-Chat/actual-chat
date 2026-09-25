namespace ActualChat.UI.Blazor.Components;

#pragma warning disable CA1721

public class DiveInModalPageContext
{
    private readonly IDiveInModalContext _modalContext;
    private readonly DiveInDialogPage _page;

    public object? Model => _page.Model;
    public MutablePropertyBag Items { get; } = new();
    public MutablePropertyBag ContextItems => _modalContext.Items;
    public bool IsInnerStep => _modalContext.IsInnerStep;
    public bool IsTopmostPage(Type pageComponentType) => _modalContext.IsTopmostPage(pageComponentType);

    public string Title {
        get;
        set {
            if (Title == value)
                return;

            field = value ?? throw new ArgumentOutOfRangeException(nameof(value));
            StateHasChanged();
        }
    } = "";

    public string Class {
        get;
        set {
            if (field == value)
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

    // Custom header content (a hero) the start page hoists into the fixed header region.
    // Re-render is triggered only on the null <-> non-null transition: the page reassigns a fresh
    // fragment every render, and firing StateHasChanged on each would loop the frame.
    public RenderFragment? Header {
        get;
        set {
            var wasNull = field == null;
            field = value;
            if (wasNull != (value == null))
                StateHasChanged();
        }
    }

    // Modal-level footer content (share actions, a comment editor) the page hoists above the buttons,
    // so the body stays pure scrollable content. Same null <-> non-null re-render gate as Header.
    public RenderFragment? Footer {
        get;
        set {
            var wasNull = field == null;
            field = value;
            if (wasNull != (value == null))
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

    public void Refresh()
        => StateHasChanged();

    private void StateHasChanged()
        => _modalContext.StateHasChanged();
}
