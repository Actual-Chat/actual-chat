// <auto-generated />
using ActualChat.Mjml.Blazor.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.Mjml.Blazor.Components;

public class MjmlDivider : ComponentBase
{
    [Parameter] public MjmlDividerAlign? Align { get; set; }
    [Parameter] public string? BorderColor { get; set; }
    [Parameter] public string? BorderStyle { get; set; }
    [Parameter] public string? BorderWidth { get; set; }
    [Parameter] public string? ContainerBackgroundColor { get; set; }
    [Parameter] public string? CssClass { get; set; }
    [Parameter] public string? MjmlClass { get; set; }
    [Parameter] public string? Padding { get; set; }
    [Parameter] public string? PaddingBottom { get; set; }
    [Parameter] public string? PaddingLeft { get; set; }
    [Parameter] public string? PaddingRight { get; set; }
    [Parameter] public string? PaddingTop { get; set; }
    [Parameter] public string? Width { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "mj-divider");
        if (Align is not null)
            builder.AddAttribute(1, "align", Align.Value.ToMjmlValue());
        if (BorderColor is not null)
            builder.AddAttribute(2, "border-color", BorderColor);
        if (BorderStyle is not null)
            builder.AddAttribute(3, "border-style", BorderStyle);
        if (BorderWidth is not null)
            builder.AddAttribute(4, "border-width", BorderWidth);
        if (ContainerBackgroundColor is not null)
            builder.AddAttribute(5, "container-background-color", ContainerBackgroundColor);
        if (CssClass is not null)
            builder.AddAttribute(6, "css-class", CssClass);
        if (MjmlClass is not null)
            builder.AddAttribute(7, "mj-class", MjmlClass);
        if (Padding is not null)
            builder.AddAttribute(8, "padding", Padding);
        if (PaddingBottom is not null)
            builder.AddAttribute(9, "padding-bottom", PaddingBottom);
        if (PaddingLeft is not null)
            builder.AddAttribute(10, "padding-left", PaddingLeft);
        if (PaddingRight is not null)
            builder.AddAttribute(11, "padding-right", PaddingRight);
        if (PaddingTop is not null)
            builder.AddAttribute(12, "padding-top", PaddingTop);
        if (Width is not null)
            builder.AddAttribute(13, "width", Width);
        builder.CloseElement();
    }
}
