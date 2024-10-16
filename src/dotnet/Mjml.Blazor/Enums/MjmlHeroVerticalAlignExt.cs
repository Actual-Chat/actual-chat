// <auto-generated />
namespace ActualChat.Mjml.Blazor.Enums;

public static class MjmlHeroVerticalAlignExt
{
    public static string ToMjmlValue(this MjmlHeroVerticalAlign value)
        => value switch {
            MjmlHeroVerticalAlign.Top => "top",
            MjmlHeroVerticalAlign.Bottom => "bottom",
            MjmlHeroVerticalAlign.Middle => "middle",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };
}
