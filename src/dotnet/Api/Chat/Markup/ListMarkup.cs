using Cysharp.Text;

namespace ActualChat.Chat;

public record ListItemMarkup(Markup Content, bool Ordered, int? Order = null) : Markup
{
    public override string Format()
        => GetPrefix() + Content.Format();

    private string GetPrefix()
        => Ordered ? $"{Order}. " : "- ";
}

public record ListMarkup : Markup
{
    public bool IsOrdered { get; }
    public ImmutableArray<ListItemMarkup> Items { get; init; }

    public ListMarkup(ImmutableArray<ListItemMarkup> items)
    {
        if (items.Length <= 0)
            throw new ArgumentException("item list should contain at least 1 item", nameof(items));

        IsOrdered = items.All(c => c.Ordered);
        Items = items;
    }

    public ListMarkup(IEnumerable<ListItemMarkup> items) : this([..items]) { }

    public override string Format()
    {
        using var sb = ZString.CreateStringBuilder();
        foreach (var item in Items)
            sb.AppendLine(item.Format());
        return sb.ToString();
    }

    public void Deconstruct(out ImmutableArray<ListItemMarkup> items)
        => items = Items;
}
