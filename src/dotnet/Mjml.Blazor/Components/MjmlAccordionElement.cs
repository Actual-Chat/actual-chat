// <auto-generated />
using ActualChat.Mjml.Blazor.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.Mjml.Blazor.Components;

public class MjmlAccordionElement : ComponentBase
{
    [Parameter] public string? BackgroundColor { get; set; }
    [Parameter] public string? Border { get; set; }
    [Parameter] public string? CssClass { get; set; }
    [Parameter] public string? FontFamily { get; set; }
    [Parameter] public MjmlAccordionElementIconAlign? IconAlign { get; set; }
    [Parameter] public string? IconHeight { get; set; }
    [Parameter] public MjmlAccordionElementIconPosition? IconPosition { get; set; }
    [Parameter] public string? IconUnwrappedAlt { get; set; }
    [Parameter] public string? IconUnwrappedUrl { get; set; }
    [Parameter] public string? IconWidth { get; set; }
    [Parameter] public string? IconWrappedAlt { get; set; }
    [Parameter] public string? IconWrappedUrl { get; set; }
    [Parameter] public string? MjmlClass { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "mj-accordion-element");
        if (BackgroundColor is not null)
            builder.AddAttribute(1, "background-color", BackgroundColor);
        if (Border is not null)
            builder.AddAttribute(2, "border", Border);
        if (CssClass is not null)
            builder.AddAttribute(3, "css-class", CssClass);
        if (FontFamily is not null)
            builder.AddAttribute(4, "font-family", FontFamily);
        if (IconAlign is not null)
            builder.AddAttribute(5, "icon-align", IconAlign.Value.ToMjmlValue());
        if (IconHeight is not null)
            builder.AddAttribute(6, "icon-height", IconHeight);
        if (IconPosition is not null)
            builder.AddAttribute(7, "icon-position", IconPosition.Value.ToMjmlValue());
        if (IconUnwrappedAlt is not null)
            builder.AddAttribute(8, "icon-unwrapped-alt", IconUnwrappedAlt);
        if (IconUnwrappedUrl is not null)
            builder.AddAttribute(9, "icon-unwrapped-url", IconUnwrappedUrl);
        if (IconWidth is not null)
            builder.AddAttribute(10, "icon-width", IconWidth);
        if (IconWrappedAlt is not null)
            builder.AddAttribute(11, "icon-wrapped-alt", IconWrappedAlt);
        if (IconWrappedUrl is not null)
            builder.AddAttribute(12, "icon-wrapped-url", IconWrappedUrl);
        if (MjmlClass is not null)
            builder.AddAttribute(13, "mj-class", MjmlClass);
        if (ChildContent is not null)
            builder.AddContent(14, ChildContent);
        builder.CloseElement();
    }
}
