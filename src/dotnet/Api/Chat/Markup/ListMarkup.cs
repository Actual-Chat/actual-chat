using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents an unordered list of items in markup.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class ListMarkup : BlockMarkup
{
    [DataMember, Key(0)]
    public ListItemMarkup[] Items { get; init; } // Immutable!

    public ListMarkup(ListItemMarkup[] items)
    {
        if (items.Length == 0)
            throw new ArgumentException("item list should contain at least 1 item", nameof(items));

        Items = items;
    }

    public ListMarkup(IEnumerable<ListItemMarkup> items)
        : this(items.ToArray()) { }

    public override string Format()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        foreach (var item in Items) {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(item.Format());
        }
        return sb.ToStringAndRelease();
    }

    public override Markup Simplify()
    {
        var items = new List<ListItemMarkup>();
        var isSimplified = false;
        foreach (var originalItem in Items) {
            var item = (ListItemMarkup)originalItem.Simplify();
            if (!ReferenceEquals(item, originalItem))
                isSimplified = true;
            items.Add(item);
        }
        return !isSimplified ? this : new ListMarkup(items);
    }
}
