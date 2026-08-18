namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// Which way <see cref="InfiniteList{TItem}"/> lays out its scroll space: from the top down, or
/// bottom-up via <c>column-reverse</c>. Fixed for the life of a list — see the JS side for why.
/// </summary>
public enum VirtualListRenderDirection
{
    Natural = 0,
    // What the chat uses: the container is anchored by its bottom edge, so an item growing in place at
    // the end keeps the newest content flush without anything having to correct for it.
    Reverse = 1,
}
