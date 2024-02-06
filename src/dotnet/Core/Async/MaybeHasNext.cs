namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
public record struct MaybeHasNext<TItem>(TItem Item, bool HasNext);
