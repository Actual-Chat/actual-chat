namespace ActualChat.UI.Blazor.Components;

public sealed record ChartItem(string Label, double Value, string Class = "", string? Hint = null, bool HasGap = false);
