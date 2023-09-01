namespace ActualChat.UI.Blazor.Components;

public class DataBag
{
    private readonly Dictionary<string, object> _items = new (StringComparer.Ordinal);

    public IEnumerable<string> Keys
        => _items.Keys;
    public IEnumerable<object> Values
        => _items.Values;
    public int Count
        => _items.Count;

    public object? Get(string key)
        => Get(key, default);

    public object? Get(string key, object? @default)
    {
        if (!_items.TryGetValue(key, out var value))
            return @default;
        return value;
    }

    public TItem? Get<TItem>(string key)
        => Get<TItem?>(key, default);

    public TItem Get<TItem>(string key, TItem @default)
    {
        if (!_items.TryGetValue(key, out var value))
            return @default;
        return (TItem)value;
    }

    public void Set(string key, object value)
        => _items[key] = value;
}
