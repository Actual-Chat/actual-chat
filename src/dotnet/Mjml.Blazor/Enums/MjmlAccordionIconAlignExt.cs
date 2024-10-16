// <auto-generated />
namespace ActualChat.Mjml.Blazor.Enums;

public static class MjmlAccordionIconAlignExt
{
    public static string ToMjmlValue(this MjmlAccordionIconAlign value)
        => value switch {
            MjmlAccordionIconAlign.Top => "top",
            MjmlAccordionIconAlign.Middle => "middle",
            MjmlAccordionIconAlign.Bottom => "bottom",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };
}
