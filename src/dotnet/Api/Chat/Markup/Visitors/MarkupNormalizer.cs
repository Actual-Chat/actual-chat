namespace ActualChat.Chat;

/// <summary>
/// Caps blank line runs at one and drops the leading and trailing ones.
/// Blank lines inside a code block are untouched - its content is opaque here.
/// </summary>
public sealed record MarkupNormalizer : MarkupRewriter<Unit>
{
    public static readonly MarkupNormalizer Instance = new();

    public Markup Normalize(Markup markup)
    {
        var state = default(Unit);
        return Visit(markup, ref state);
    }

    protected override Markup VisitListItem(ListItemMarkup markup, ref Unit state)
    {
        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new ListItemMarkup(newContent);
    }

    protected override Markup VisitSeq(MarkupSeq markup, ref Unit state)
    {
        // An EmptyPara isn't a blank line by itself: MarkupSeqFormatHelper already emits one
        // between two non-empty paragraphs, so there the allowance is 0 rather than 1.
        var newItems = new List<Markup>(markup.Items.Length);
        var isUnchanged = true;
        var prev = (Markup?)null;
        var blankCount = 0;
        foreach (var item in markup.Items) {
            var newItem = Visit(item, ref state);
            isUnchanged &= ReferenceEquals(newItem, item);
            if (newItem is ParagraphMarkup { IsEmpty: true }) {
                blankCount++;
                continue;
            }

            var maxBlankCount = prev == null ? 0
                : IsNonEmptyPara(prev) && IsNonEmptyPara(newItem) ? 0
                : 1;
            var newBlankCount = Math.Min(blankCount, maxBlankCount);
            isUnchanged &= newBlankCount == blankCount;
            for (var i = 0; i < newBlankCount; i++)
                newItems.Add(ParagraphMarkup.Empty);
            blankCount = 0;
            newItems.Add(newItem);
            prev = newItem;
        }
        isUnchanged &= blankCount == 0;
        if (isUnchanged)
            return markup;

        return newItems.Count switch {
            0 => Markup.EmptyText,
            1 => newItems[0],
            _ => new MarkupSeq(newItems.ToArray()),
        };
    }

    private static bool IsNonEmptyPara(Markup markup)
        => markup is ParagraphMarkup { IsEmpty: false };
}
