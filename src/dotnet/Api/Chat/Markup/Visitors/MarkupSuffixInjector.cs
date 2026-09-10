namespace ActualChat.Chat;

/// <summary>
/// Appends a <see cref="MarkupSuffix"/> to the end of the last thing a message renders, so the
/// trailing marks sit on its last line.
/// </summary>
/// <remarks>
/// Only the spine down to that point is rebuilt - every other subtree is reused by reference, which
/// is what keeps <see cref="ByRefParameterComparer"/> from re-rendering it. So the cost is the depth
/// of the tree, not its size.
/// </remarks>
public static class MarkupSuffixInjector
{
    public static Markup Inject(Markup markup)
        => Append(markup);

    private static Markup Append(Markup markup)
    {
        switch (markup) {
        case MarkupSeq seq: {
            var items = seq.Items;
            var last = LastRenderedIndex(items);
            if (last < 0)
                return new MarkupSeq(markup, MarkupSuffix.Instance);

            var newItems = items.ToArray();
            newItems[last] = Append(items[last]);
            return new MarkupSeq(newItems);
        }
        case ParagraphMarkup paragraph:
            return new ParagraphMarkup(Append(paragraph.Content));
        case HeaderMarkup header:
            return new HeaderMarkup(header.Level, Append(header.Content));
        case BlockQuoteMarkup blockQuote:
            return new BlockQuoteMarkup(Append(blockQuote.Content));
        case ListMarkup list when list.Items.Length != 0: {
            var items = list.Items;
            var newItems = items.ToArray();
            newItems[^1] = new ListItemMarkup(Append(items[^1].Content));
            return new ListMarkup(newItems);
        }
        default:
            // A leaf, or a block that can't hold inline content (a code block, a table). Pairing it
            // with the suffix rather than growing the parent keeps every allocation here small, and
            // a nested MarkupSeq renders exactly as its items do.
            return new MarkupSeq(markup, MarkupSuffix.Instance);
        }
    }

    // MarkupSeqView drops trailing blank paragraphs, so the suffix must not land in one.
    private static int LastRenderedIndex(Markup[] items)
    {
        for (var i = items.Length - 1; i >= 0; i--)
            if (items[i] is not ParagraphMarkup { IsEmpty: true })
                return i;

        return items.Length - 1;
    }
}
