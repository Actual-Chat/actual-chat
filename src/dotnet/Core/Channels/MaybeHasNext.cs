namespace ActualChat.Channels;

[StructLayout(LayoutKind.Auto)]
public record struct MaybeHasNext<TItem>(TItem Item, bool HasNext);
