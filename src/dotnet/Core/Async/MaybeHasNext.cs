namespace ActualChat;

/// <summary>
/// Pairs an item with a flag indicating whether more items follow in a sequence.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public record struct MaybeHasNext<TItem>(TItem Item, bool HasNext);
