namespace ActualChat.UI.Blazor;

public readonly struct AttributeMapBuilder
{
    public Dictionary<string, object> Map { get; init; }

    public static AttributeMapBuilder New()
        => new(new Dictionary<string, object>(StringComparer.Ordinal));

    public static AttributeMapBuilder From(IReadOnlyDictionary<string, object>? attributes)
        => new(attributes?.ToDictionary() ?? new Dictionary<string, object>(StringComparer.Ordinal));

    public static AttributeMapBuilder From(Dictionary<string, object>? attributes)
        => new(attributes ?? new Dictionary<string, object>(StringComparer.Ordinal));

    private AttributeMapBuilder(Dictionary<string, object> map)
        => Map = map;

    // Conversion

    public override string ToString()
    {
        var map = Map.Select(kv => $"{kv.Key}={JsonFormatter.Format(kv.Value)}").ToDelimitedString();
        return $"{{ {map} }}";
    }

    // With and Without

    public AttributeMapBuilder With(string name, object value)
    {
        var map = Map;
        map[name] = value;
        return new AttributeMapBuilder(map);
    }

    public AttributeMapBuilder Without(string name)
    {
        var map = Map;
        map.Remove(name);
        return new AttributeMapBuilder(map);
    }

    // Other helpers

    public AttributeMapBuilder OnClick(object receiver, EventCallback click)
        => click.HasDelegate
            ? With("onclick", EventCallback.Factory.Create<MouseEventArgs>(receiver, click))
            : this;

    public AttributeMapBuilder OnClick(object receiver, EventCallback<MouseEventArgs> click)
        => click.HasDelegate
            ? With("onclick", EventCallback.Factory.Create(receiver, click))
            : this;
}
