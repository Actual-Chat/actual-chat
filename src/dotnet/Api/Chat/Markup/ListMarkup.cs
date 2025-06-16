using ActualLab.Fusion.Blazor;
using Cysharp.Text;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class ListMarkup : Markup
{
    public bool IsOrdered { get; }
    public ListItemMarkup[] Items { get; init; } // Immutable!

    public ListMarkup(ListItemMarkup[] items)
    {
        if (items.Length == 0)
            throw new ArgumentException("item list should contain at least 1 item", nameof(items));

        Items = items;
        IsOrdered = items.All(c => c.Order.HasValue);
    }

    public ListMarkup(IEnumerable<ListItemMarkup> items)
        : this(items.ToArray()) { }

    public override string Format()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        foreach (var item in Items)
            sb.AppendLine(item.Format());
        return sb.ToStringAndRelease();
    }
}
