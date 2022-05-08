namespace ActualChat.UI.Blazor.Components;

public enum FlexAlignment { Start = 0, Center, End }
public enum FlexDirection { Horizontal = 0, Vertical = 1 }

[StructLayout(LayoutKind.Auto)]
public readonly record struct FlexMode(
    FlexAlignment Alignment = FlexAlignment.Start,
    FlexDirection Direction = FlexDirection.Horizontal)
{
    private static volatile Dictionary<FlexMode, string> _cache = new();

    public FlexAlignment ItemAlignment { get; init; } = FlexAlignment.Center;
    public bool IsReversed { get; init; } = false;

    public override string ToString()
    {
        if (_cache.TryGetValue(this, out var result)) return result;

        result = "flex ";
        result += Direction == FlexDirection.Vertical ? "flex-col" : "flex-row";
        result += IsReversed ? "-reversed " : " ";
        result += Alignment switch {
            FlexAlignment.Start => "justify-start ",
            FlexAlignment.Center => "justify-center ",
            FlexAlignment.End => "justify-end ",
            _ => "",
        };
        result += ItemAlignment switch {
            FlexAlignment.Start => "item-start",
            FlexAlignment.Center => "items-center",
            FlexAlignment.End => "items-end",
            _ => "",
        };

        _cache = new Dictionary<FlexMode, string>(_cache) { { this, result } };
        return result;
    }
}
