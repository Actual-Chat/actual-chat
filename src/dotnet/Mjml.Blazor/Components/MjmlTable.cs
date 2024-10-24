// <auto-generated />
using ActualChat.Mjml.Blazor.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.Mjml.Blazor.Components;

public class MjmlTable : ComponentBase
{
    [Parameter] public MjmlTableAlign? Align { get; set; }
    [Parameter] public string? Border { get; set; }
    [Parameter] public int? Cellpadding { get; set; }
    [Parameter] public int? Cellspacing { get; set; }
    [Parameter] public string? Color { get; set; }
    [Parameter] public string? ContainerBackgroundColor { get; set; }
    [Parameter] public string? CssClass { get; set; }
    [Parameter] public string? FontFamily { get; set; }
    [Parameter] public string? FontSize { get; set; }
    [Parameter] public string? FontWeight { get; set; }
    [Parameter] public string? LineHeight { get; set; }
    [Parameter] public string? MjmlClass { get; set; }
    [Parameter] public string? Padding { get; set; }
    [Parameter] public string? PaddingBottom { get; set; }
    [Parameter] public string? PaddingLeft { get; set; }
    [Parameter] public string? PaddingRight { get; set; }
    [Parameter] public string? PaddingTop { get; set; }
    [Parameter] public MjmlTableRole? Role { get; set; }
    [Parameter] public MjmlTableTableLayout? TableLayout { get; set; }
    [Parameter] public MjmlTableVerticalAlign? VerticalAlign { get; set; }
    [Parameter] public string? Width { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "mj-table");
        if (Align is not null)
            builder.AddAttribute(1, "align", Align.Value.ToMjmlValue());
        if (Border is not null)
            builder.AddAttribute(2, "border", Border);
        if (Cellpadding is not null)
            builder.AddAttribute(3, "cellpadding", Cellpadding);
        if (Cellspacing is not null)
            builder.AddAttribute(4, "cellspacing", Cellspacing);
        if (Color is not null)
            builder.AddAttribute(5, "color", Color);
        if (ContainerBackgroundColor is not null)
            builder.AddAttribute(6, "container-background-color", ContainerBackgroundColor);
        if (CssClass is not null)
            builder.AddAttribute(7, "css-class", CssClass);
        if (FontFamily is not null)
            builder.AddAttribute(8, "font-family", FontFamily);
        if (FontSize is not null)
            builder.AddAttribute(9, "font-size", FontSize);
        if (FontWeight is not null)
            builder.AddAttribute(10, "font-weight", FontWeight);
        if (LineHeight is not null)
            builder.AddAttribute(11, "line-height", LineHeight);
        if (MjmlClass is not null)
            builder.AddAttribute(12, "mj-class", MjmlClass);
        if (Padding is not null)
            builder.AddAttribute(13, "padding", Padding);
        if (PaddingBottom is not null)
            builder.AddAttribute(14, "padding-bottom", PaddingBottom);
        if (PaddingLeft is not null)
            builder.AddAttribute(15, "padding-left", PaddingLeft);
        if (PaddingRight is not null)
            builder.AddAttribute(16, "padding-right", PaddingRight);
        if (PaddingTop is not null)
            builder.AddAttribute(17, "padding-top", PaddingTop);
        if (Role is not null)
            builder.AddAttribute(18, "role", Role.Value.ToMjmlValue());
        if (TableLayout is not null)
            builder.AddAttribute(19, "table-layout", TableLayout.Value.ToMjmlValue());
        if (VerticalAlign is not null)
            builder.AddAttribute(20, "vertical-align", VerticalAlign.Value.ToMjmlValue());
        if (Width is not null)
            builder.AddAttribute(21, "width", Width);
        if (ChildContent is not null)
            builder.AddContent(22, ChildContent);
        builder.CloseElement();
    }
}
