using ActualChat.UI.Blazor.App.Components.MarkupParts;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class MarkupView : MarkupViewBase<Markup>
{
    private TypeMapper<IMarkupView> ViewResolver => field ??= Hub.Services.GetRequiredService<TypeMapper<IMarkupView>>();

    public MarkupView() { }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var componentType = ViewResolver.TryGet(Markup.GetType()) ?? typeof(UnknownMarkupView);
        builder.OpenComponent(0, componentType);
        builder.AddAttribute(1, nameof(IMarkupView.Markup), Markup);
        builder.CloseComponent();
    }
}
