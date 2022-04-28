using Microsoft.AspNetCore.Components.Rendering;
using Stl.Extensibility;

namespace ActualChat.Chat.UI.Blazor.Components.MarkupParts;

public class MarkupView : MarkupViewBase<Markup>
{
    [Inject] private IMatchingTypeFinder MatchingTypeFinder { get; init; } = null!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var componentType =
            MatchingTypeFinder.TryFind(Markup.GetType(), typeof(IMarkupView))
            ?? typeof(UnknownMarkupView);

        builder.OpenComponent(0, componentType);
        builder.AddAttribute(1, nameof(IMarkupView.Markup), Markup);
        builder.CloseComponent();
    }
}
