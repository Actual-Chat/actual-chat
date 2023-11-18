namespace ActualChat.Chat;

#pragma warning disable CA1724 // The type name Markup conflicts in whole or in part with the namespace ...

public abstract record Markup
{
    public static Markup Empty => PlainTextMarkup.Empty;

    public static Markup Join(Markup first, Markup second)
    {
        if (first == Empty)
            return second;
        if (second == Empty)
            return first;
        if (first is MarkupSeq f) {
            if (second is MarkupSeq s)
                return new MarkupSeq(f.Items.AddRange(s.Items));
            return new MarkupSeq(f.Items.Append(second));
        }
        else if (second is MarkupSeq s)
            return new MarkupSeq(ImmutableArray<Markup>.Empty.Add(first).AddRange(s.Items));
        return new MarkupSeq(first, second);
    }

    public static Markup Join(IEnumerable<Markup> parts)
    {
        var items = new List<Markup>();
        foreach (var markup in parts) {
            if (markup is MarkupSeq seq)
                items.AddRange(seq.Items);
            else if (markup != Empty)
                items.Add(markup);
        }
        return items.Count switch {
            0 => Empty,
            1 => items[0],
            _ => new MarkupSeq(items),
        };
    }

    public abstract string Format();

    public virtual Markup Simplify()
        => this;

    // Operators

    public static Markup operator +(Markup first, Markup second)
        => Join(first, second);
}
