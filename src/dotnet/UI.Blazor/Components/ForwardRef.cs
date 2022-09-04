namespace ActualChat.UI.Blazor.Components;

public class ForwardRef
{
    private ElementReference _ref;

    public ElementReference Current
    {
        get => _ref;
        set => SetValue(value);
    }

    public void SetValue(ElementReference value)
        => _ref = value;
}
