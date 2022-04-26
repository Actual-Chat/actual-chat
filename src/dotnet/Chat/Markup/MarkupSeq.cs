using System.Text;
using Cysharp.Text;

namespace ActualChat.Chat;

public sealed record MarkupSeq(ImmutableArray<Markup> Items) : Markup
{
    public MarkupSeq(params Markup[] items) : this(ImmutableArray.Create(items)) { }
    public MarkupSeq(IEnumerable<Markup> items) : this(ImmutableArray.Create(items.ToArray())) { }
    public MarkupSeq() : this(ImmutableArray<Markup>.Empty) { }

    public override string ToMarkupText()
    {
        using var sb = ZString.CreateStringBuilder();
        foreach (var item in Items)
            sb.Append(item.ToMarkupText());
        return sb.ToString();
    }

    public override Markup Simplify()
    {
        if (Items.Length == 1)
            return Items[0].Simplify();

        var items = new List<Markup>();
        var lastPlainText = (PlainTextMarkup?) null;
        var isSimplified = false;
        foreach (var originalItem in Items) {
            var item = originalItem.Simplify();
            if (!ReferenceEquals(item, originalItem))
                isSimplified = true;

            if (item.GetType() != typeof(PlainTextMarkup)) {
                if (lastPlainText != null)
                    items.Add(lastPlainText);
                lastPlainText = null;
                items.Add(item);
            } else if (item is PlainTextMarkup pt) { // Always true
                if (lastPlainText == null)
                    lastPlainText = pt;
                else {
                    lastPlainText = lastPlainText with { Text = lastPlainText.Text + pt.Text };
                    isSimplified = true;
                }
            }
        }
        if (lastPlainText != null)
            items.Add(lastPlainText);

        if (!isSimplified)
            return this;
        return items.Count switch {
            0 => Markup.Empty,
            1 => items[0],
            _ => new MarkupSeq(items),
        };
    }

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Items = [");
        builder.Append(Items.ToDelimitedString(", "));
        builder.Append("]");
        return true;
    }
}

