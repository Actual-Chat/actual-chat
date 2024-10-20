// <auto-generated />
namespace ActualChat.Mjml.Blazor.Enums;

public static class MjmlAccordionIconPositionExt
{
    public static string ToMjmlValue(this MjmlAccordionIconPosition value)
        => value switch {
            MjmlAccordionIconPosition.Left => "left",
            MjmlAccordionIconPosition.Right => "right",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };
}
