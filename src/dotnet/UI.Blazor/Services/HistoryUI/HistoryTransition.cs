using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public readonly record struct HistoryTransition(
    HistoryItem Item,
    HistoryItem PrevItem,
    LocationChangeKind LocationChangeKind)
{
    public bool IsUriChanged => !OrdinalEquals(Item.Uri, PrevItem.Uri);

    public override string ToString()
        => $"{GetType().Name}({Item} <- {PrevItem}, {LocationChangeKind})";
}
