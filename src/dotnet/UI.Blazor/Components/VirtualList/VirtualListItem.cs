using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.Components;

public class VirtualListItem<TItem> : ComponentBase
    where TItem : IVirtualListItem
{
    [Parameter, EditorRequired] public TItem Item { get; set; } = default!;
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public RenderFragment<TItem>? Content { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        /* Renders, but w/o region for Content:

        <li id="@item.Key"
            @key="item.Key"
            class="@itemClass"
            data-count-as="@item.CountAs">
            @Item(item)
        </li>
         */
 #pragma warning disable MA0123
        var i = 0;
        builder.OpenElement(i++, "li");
        builder.AddAttribute(i++, "id", Item.Key.Value);
        builder.AddAttribute(i++, "class", Class);
        builder.AddAttribute(i++, "data-count-as", Item.CountAs);
        var contentBuilder = Content?.Invoke(Item);
        contentBuilder?.Invoke(builder);
        builder.CloseElement();
 #pragma warning restore MA0123
    }
}
