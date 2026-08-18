using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Components;

public static class InfiniteList
{
    // Wrapper height of an infinite (scrollbar-less) list. Must match InfiniteSize in infinite-list.ts.
    public const double InfiniteSize = 4_000_000;
    public static readonly string JSCreateMethod = $"{BlazorUICoreModule.ImportName}.InfiniteList.create";
}

/// <summary>
/// A virtualized list of unbounded length: no scrollbar, a fixed huge virtual scroll space, and
/// items held in place by anchoring. Used for chat messages and other unbounded feeds.
/// </summary>
public sealed partial class InfiniteList<TItem>
    where TItem : class, IVirtualListItem
{
    [Parameter] public double SpacerSize { get; set; } = 1000;
    [Parameter] public VirtualListEdge DefaultEdge { get; set; }
    [Parameter] public VirtualListRenderDirection RenderDirection { get; set; }
    [Parameter] public bool AnimateItemHeight { get; set; }
    [Parameter] public bool ShowOverscrollCue { get; set; }
    [Parameter] public int RetainedItemCount { get; set; } = 5;

    protected override ValueTask<IJSObjectReference> CreateJSRef()
        => JS.InvokeAsync<IJSObjectReference>(InfiniteList.JSCreateMethod,
            Ref,
            BlazorRef,
            Identity,
            DefaultEdge,
            RenderDirection,
            AnimateItemHeight,
            SpacerSize,
            ExpandMultiplier,
            RetainedItemCount);
}
