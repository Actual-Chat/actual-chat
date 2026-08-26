namespace ActualChat.UI.Blazor.Components;

public static class TooltipSeverityExt
{
    public static string ToSeverityString(this TooltipSeverity severity)
        => severity switch {
            TooltipSeverity.Normal => "",
            TooltipSeverity.Error => "error",
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
        };
}
