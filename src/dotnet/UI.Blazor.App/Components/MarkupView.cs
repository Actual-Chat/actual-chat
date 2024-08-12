using System.Diagnostics.CodeAnalysis;
using ActualChat.UI.Blazor.App.Components.MarkupParts;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.App.Components;

public class MarkupView : MarkupViewBase<Markup>
{
    [Inject] private TypeMapper<IMarkupView> ViewResolver { get; init; } = null!;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MarkupViewBase<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ComputedMarkupViewBase<,>))]
    public MarkupView() { }

#pragma warning disable IL2072
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var componentType = ViewResolver.TryGet(Markup.GetType())
            ?? typeof(UnknownMarkupView);

        builder.OpenComponent(0, componentType);
        builder.AddAttribute(1, nameof(IMarkupView.Markup), Markup);
        builder.CloseComponent();
    }
#pragma warning restore IL2072
}
