using System.Text;

namespace ActualChat.Chat;

internal static class MarkupSeqFormatHelper
{
    // Number of newlines emitted around an item is determined uniformly by both
    // MarkupSeq.Format() and the visitor-based VisitSeq in MarkupFormatterBase,
    // so a round-trip through MarkupParser preserves the exact blank-line count.
    //
    // The model:
    // - Each EmptyPara stands for one blank line (or, when leading/trailing, for
    //   one extra "\n" beyond the natural block boundary).
    // - Between two non-empty paragraphs we need a paragraph break ("\n\n").
    // - Otherwise blocks are joined by a single "\n", and each intervening
    //   EmptyPara adds one more "\n" to that join.
    // - A trailing EmptyPara after a non-empty paragraph requires an extra "\n"
    //   because the paragraph break "\n\n" is needed to separate the paragraph
    //   from the blank line that the EmptyPara represents.

    public static void FormatBlockSequence(IReadOnlyList<Markup> items, StringBuilder sb)
    {
        // Inline sequences (e.g. the content of a single paragraph) contain no
        // BlockMarkup items, so they must concatenate without any newlines.
        var hasAnyBlock = false;
        foreach (var item in items) {
            if (item.IsBlockMarkup()) {
                hasAnyBlock = true;
                break;
            }
        }
        if (!hasAnyBlock) {
            foreach (var item in items)
                sb.Append(item.Format());
            return;
        }

        Markup? prevNonEmpty = null;
        var emptyParaCount = 0;
        foreach (var item in items) {
            if (IsEmptyPara(item)) {
                emptyParaCount++;
                continue;
            }

            if (prevNonEmpty != null) {
                var newlines = 1; // block separator
                if (IsNonEmptyPara(prevNonEmpty) && IsNonEmptyPara(item))
                    newlines++; // paragraph break
                newlines += emptyParaCount;
                AppendNewLines(sb, newlines);
            }

            sb.Append(item.Format());
            prevNonEmpty = item;
            emptyParaCount = 0;
        }

        if (emptyParaCount > 0) {
            // Trailing EmptyParas: one newline per EmptyPara plus, when the
            // last non-empty item is a paragraph, one extra newline to make the
            // first trailing blank line visible (the paragraph "owns" its line,
            // the EmptyPara needs its own line on top of that).
            var trailingNewlines = emptyParaCount;
            if (prevNonEmpty != null && IsNonEmptyPara(prevNonEmpty))
                trailingNewlines++;
            else if (prevNonEmpty == null)
                // Sequence is all EmptyParas: emit (count-1) newlines.
                // The first EmptyPara is the start of input, no leading newline.
                trailingNewlines--;
            AppendNewLines(sb, trailingNewlines);
        }
    }

    public static bool IsEmptyPara(Markup m)
        => m is ParagraphMarkup p && p.Content == Markup.EmptyText;

    public static bool IsNonEmptyPara(Markup m)
        => m is ParagraphMarkup p && p.Content != Markup.EmptyText;

    private static void AppendNewLines(StringBuilder sb, int count)
    {
        for (var i = 0; i < count; i++)
            sb.Append(NewLineMarkup.Instance.Format());
    }
}
