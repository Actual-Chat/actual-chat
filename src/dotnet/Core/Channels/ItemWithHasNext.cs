namespace ActualChat.Channels;

public record struct ItemWithHasNext<TItem>(TItem Item, bool HasNext);
