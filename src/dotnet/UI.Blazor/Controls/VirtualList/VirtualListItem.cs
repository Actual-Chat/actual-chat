namespace ActualChat.UI.Blazor.Controls;

public readonly struct VirtualListItem<TItem>
{
    public string Key { get; }
    public TItem Item { get; }
    public int CountAs { get; }

    public VirtualListItem(string key, TItem item, int countAs = 1)
    {
        Key = key;
        Item = item;
        CountAs = countAs;
    }

    public void Deconstruct(out string key, out TItem item, out int countAs)
    {
        key = Key;
        item = Item;
        countAs = CountAs;
    }

    public void Deconstruct(out string key, out TItem item)
    {
        key = Key;
        item = Item;
    }

    public override string ToString()
        => CountAs == 1 ? $"(#{Key} -> {Item})" : $"(#{Key} -> {Item}, CountAs = {CountAs})";

    // Operators

    public static implicit operator VirtualListItem<TItem>(KeyValuePair<string, TItem> pair)
        => new(pair.Key, pair.Value);
    public static implicit operator VirtualListItem<TItem>((string Key, TItem Item) pair)
        => new(pair.Key, pair.Item);
    public static implicit operator VirtualListItem<TItem>((string Key, TItem Item, int CountAs) triplet)
        => new(triplet.Key, triplet.Item, triplet.CountAs);
}
