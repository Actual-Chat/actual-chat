// <auto-generated />
namespace ActualChat.Mjml.Blazor.Enums;

public static class MjmlTableTableLayoutExt
{
    public static string ToMjmlValue(this MjmlTableTableLayout value)
        => value switch {
            MjmlTableTableLayout.Auto => "auto",
            MjmlTableTableLayout.Fixed => "fixed",
            MjmlTableTableLayout.Initial => "initial",
            MjmlTableTableLayout.Inherit => "inherit",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };
}
