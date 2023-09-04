namespace ActualChat.UI.Blazor.Components;

public class DiveInModalPageContext(
    IDiveInModalContext modalContext,
    DiveInDialogPage page)
{
    public object? Model => page.Model;

    public DataBag PageDataBag { get; } = new ();

    public DataBag ModalDataBag => modalContext.DataBag;

    public string Title { get; private set; } = "";

    public string Class { get; private set; } = "";

    public DialogButtonInfo[]? ButtonInfos { get; private set;  }

    public T GetTypedModel<T>()
        => (T)Model!;

    public void SetTitle(string title)
    {
        if (OrdinalEquals(Title, title))
            return;
        Title = title;
        StateHasChanged();
    }

    public void SetClass(string @class)
    {
        if (OrdinalEquals(Class, @class))
            return;
        Class = @class;
        StateHasChanged();
    }

    public void RegisterButtons(params DialogButtonInfo[] buttonInfos)
    {
        ButtonInfos = buttonInfos;
        StateHasChanged();
    }

    public void Close()
        => modalContext.Close();

    public void StepIn(DiveInDialogPage page)
        => modalContext.StepIn(page);

    private void StateHasChanged()
        => modalContext.StateHasChanged();
}
