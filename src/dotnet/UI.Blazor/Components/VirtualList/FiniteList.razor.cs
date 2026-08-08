using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Components;

public static class FiniteList
{
    public static readonly string JSCreateMethod = $"{BlazorUICoreModule.ImportName}.FiniteList.create";
}

/// <summary>
/// A virtualized list of a known length whose items are all the same height, save for a few
/// irregular ones. Item position is a pure function of index, so loading a different window
/// cannot move what is already on screen.
/// </summary>
public sealed partial class FiniteList<TItem>
    where TItem : class, IVirtualListItem
{
    protected override ValueTask<IJSObjectReference> CreateJSRef()
        => JS.InvokeAsync<IJSObjectReference>(FiniteList.JSCreateMethod,
            Ref,
            BlazorRef,
            Identity,
            ExpandMultiplier);
}
