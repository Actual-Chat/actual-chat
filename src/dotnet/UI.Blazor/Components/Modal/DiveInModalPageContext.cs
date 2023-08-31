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

    public DiveInModalPageBag Bag { get; } = new ();

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

public class DiveInModalPageBag
{
    private readonly Dictionary<string, object> _items = new (StringComparer.Ordinal);

    public IEnumerable<string> Keys
        => _items.Keys;
    public IEnumerable<object> Values
        => _items.Values;
    public int Count
        => _items.Count;

    public object? Get(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return null;
        return value;
    }

    public TItem? Get<TItem>(string key)
        where TItem : class
    {
        if (!_items.TryGetValue(key, out var value))
            return null;
        return (TItem)value;
    }

    public void Set(string key, object value)
        => _items[key] = value;
}
