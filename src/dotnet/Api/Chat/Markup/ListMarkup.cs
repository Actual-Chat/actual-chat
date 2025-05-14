using ActualLab.Fusion.Blazor;
using Cysharp.Text;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record ListMarkup : Markup
{
    public bool IsOrdered { get; }
    public ListItemMarkup[] Items { get; init; } // Immutable!

    public ListMarkup(ListItemMarkup[] items)
    {
        if (items.Length <= 0)
            throw new ArgumentException("item list should contain at least 1 item", nameof(items));

        IsOrdered = items.All(c => c.Ordered);
        Items = items;
    }

    public ListMarkup(IEnumerable<ListItemMarkup> items)
        : this(items.ToArray()) { }

    public override string Format()
    {
        using var sb = ZString.CreateStringBuilder();
        foreach (var item in Items)
            sb.AppendLine(item.Format());
        return sb.ToString();
    }

    public void Deconstruct(out ListItemMarkup[] items)
        => items = Items;

    // This record relies on referential equality
    public bool Equals(ListMarkup? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
