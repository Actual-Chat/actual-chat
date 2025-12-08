using ActualChat.UI.Blazor.App.Components.MarkupParts;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class MarkupView : MarkupViewBase<Markup>
{
    private TypeMapper<IMarkupView> ViewResolver => field ??= Hub.Services.GetRequiredService<TypeMapper<IMarkupView>>();

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MarkupViewBase<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ComputedMarkupViewBase<,>))]
    public MarkupView() { }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "UI types are expected to be untrimmed.")]
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var componentType = ViewResolver.TryGet(Markup.GetType()) ?? typeof(UnknownMarkupView);
        builder.OpenComponent(0, componentType);
        builder.AddAttribute(1, nameof(IMarkupView.Markup), Markup);
        builder.CloseComponent();
    }
}
