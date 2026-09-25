using System.Buffers;
using System.Text;

namespace ActualChat.Chat;

public interface IMarkupFormatter
{
    string Format(Markup markup);
    void FormatTo(Markup markup, StringBuilder sb);
}

public abstract record MarkupFormatterBase : MarkupVisitorWithState<StringBuilder>, IMarkupFormatter
{
    public string Format(Markup markup)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        FormatTo(markup, sb);
        return sb.ToStringAndRelease();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FormatTo(Markup markup, StringBuilder sb)
        => Visit(markup, ref sb);

    // Protected methods

    protected override void VisitList(ListMarkup markup, ref StringBuilder state)
    {
        var isFirst = true;
        foreach (var item in markup.Items) {
            if (!isFirst)
                state.AppendLine();
            VisitListItem(item, ref state);
            isFirst = false;
        }
    }

    protected override void VisitTable(TableMarkup markup, ref StringBuilder state)
    {
        VisitTableRow(markup.Header, ref state);
        state.AppendLine();
        state.Append(TableMarkup.CellSeparator);
        foreach (var alignment in markup.Alignments) {
            state.Append(' ');
            state.Append(TableMarkup.FormatDelimiterCell(alignment));
            state.Append(' ');
            state.Append(TableMarkup.CellSeparator);
        }
        foreach (var row in markup.Rows) {
            state.AppendLine();
            VisitTableRow(row, ref state);
        }
    }

    protected override void VisitTableRow(TableRowMarkup markup, ref StringBuilder state)
    {
        state.Append(TableMarkup.CellSeparator);
        foreach (var cell in markup.Cells) {
            state.Append(' ');
            VisitTableCell(cell, ref state);
            state.Append(' ');
            state.Append(TableMarkup.CellSeparator);
        }
    }

    protected override void VisitTableCell(TableCellMarkup markup, ref StringBuilder state)
    {
        var inner = ActualLab.Text.StringBuilderExt.Acquire();
        Visit(markup.Content, ref inner);
        state.Append(TableMarkup.EscapeCellText(inner.ToStringAndRelease()));
    }

    protected override void VisitSeq(MarkupSeq markup, ref StringBuilder state)
    {
        // Inline sequences (e.g. paragraph content) contain no block markup; emit
        // their items back-to-back without inserting newlines.
        var hasAnyBlock = false;
        foreach (var item in markup.Items) {
            if (item.IsBlockMarkup()) {
                hasAnyBlock = true;
                break;
            }
        }
        if (!hasAnyBlock) {
            foreach (var item in markup.Items)
                Visit(item, ref state);
            return;
        }

        Markup? prevNonEmpty = null;
        var emptyParaCount = 0;
        foreach (var item in markup.Items) {
            if (MarkupSeqFormatHelper.IsEmptyPara(item)) {
                emptyParaCount++;
                continue;
            }

            if (prevNonEmpty != null) {
                var newlines = 1;
                if (MarkupSeqFormatHelper.IsNonEmptyPara(prevNonEmpty)
                    && MarkupSeqFormatHelper.IsNonEmptyPara(item))
                    newlines++;
                newlines += emptyParaCount;
                for (var i = 0; i < newlines; i++)
                    state.Append(NewLineMarkup.Instance.Format());
            }

            Visit(item, ref state);
            prevNonEmpty = item;
            emptyParaCount = 0;
        }

        if (emptyParaCount > 0) {
            var trailingNewlines = emptyParaCount;
            if (prevNonEmpty != null && MarkupSeqFormatHelper.IsNonEmptyPara(prevNonEmpty))
                trailingNewlines++;
            else if (prevNonEmpty == null)
                trailingNewlines--;
            for (var i = 0; i < trailingNewlines; i++)
                state.Append(NewLineMarkup.Instance.Format());
        }
    }

    protected override void VisitListItem(ListItemMarkup markup, ref StringBuilder state)
    {
        state.Append(ListItemMarkup.Prefix);
        Visit(markup.Content, ref state);
    }

    protected override void VisitParagraph(ParagraphMarkup markup, ref StringBuilder state)
        => Visit(markup.Content, ref state);

    protected override void VisitHeader(HeaderMarkup markup, ref StringBuilder state)
    {
        for (var i = 0; i < markup.Level; i++)
            state.Append('#');
        state.Append(' ');
        Visit(markup.Content, ref state);
    }

    protected override void VisitBlockQuote(BlockQuoteMarkup markup, ref StringBuilder state)
    {
        var inner = ActualLab.Text.StringBuilderExt.Acquire();
        Visit(markup.Content, ref inner);
        BlockQuoteMarkup.AppendQuoted(inner.ToStringAndRelease(), state);
    }

    protected override void VisitStylized(StylizedMarkup markup, ref StringBuilder state)
    {
        state.Append(markup.StyleToken);
        Visit(markup.Content, ref state);
        state.Append(markup.StyleToken);
    }

    protected override void VisitUrl(UrlMarkup markup, ref StringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitMention(MentionMarkup markup, ref StringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref StringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitPlainText(PlainTextMarkup markup, ref StringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitPlayableText(PlayableTextMarkup markup, ref StringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref StringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitNewLine(NewLineMarkup markup, ref StringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitUnparsed(UnparsedTextMarkup markup, ref StringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitHashtag(HashtagMarkup markup, ref StringBuilder state)
        => state.Append(markup.Format());
}

public sealed record MarkupFormatter(
    Func<MentionMarkup, string> MentionFormatter,
    bool ShowStyleTokens = true,
    Func<UrlMarkup, string>? UrlFormatter = null
    ) : MarkupFormatterBase
{
    public static readonly MarkupFormatter Default = new();
    public static readonly MarkupFormatter Readable = new(MentionMarkup.ReadableFormatter);
    public static readonly MarkupFormatter ReadableUnstyled = Readable with { ShowStyleTokens = false };
    public static readonly MarkupFormatter ReadableUnstyledForQuote = ReadableUnstyled with {
        UrlFormatter = FormatUrlForQuote,
    };
    // Text for a voice: what can't be listened to (code, tables, links, hidden text) is left out,
    // every block ends a sentence so the voice pauses, and no marker or token is read aloud.
    public static readonly MarkupFormatter Spoken = new(MentionMarkup.SpokenFormatter, false) { IsSpoken = true };

    private static readonly SearchValues<char> SentenceEndChars = SearchValues.Create(".!?…:;,");

    public bool IsSpoken { get; init; }

    public MarkupFormatter() : this(MentionMarkup.DefaultFormatter, true) { }
    public MarkupFormatter(bool showStyleTokens) : this(MentionMarkup.DefaultFormatter, showStyleTokens) { }

    // Protected methods

    protected override void VisitUrl(UrlMarkup markup, ref StringBuilder state)
    {
        if (IsSpoken && markup.Kind == UrlMarkupKind.Www)
            return;

        state.Append(UrlFormatter?.Invoke(markup) ?? markup.Format());
    }

    protected override void VisitMention(MentionMarkup markup, ref StringBuilder state)
        => state.Append(MentionFormatter.Invoke(markup));

    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref StringBuilder state)
    {
        if (!IsSpoken)
            base.VisitCodeBlock(markup, ref state);
    }

    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref StringBuilder state)
    {
        if (IsSpoken)
            state.Append(markup.Text);
        else
            base.VisitPreformattedText(markup, ref state);
    }

    protected override void VisitParagraph(ParagraphMarkup markup, ref StringBuilder state)
    {
        var start = state.Length;
        base.VisitParagraph(markup, ref state);
        if (IsSpoken)
            EndSentence(state, start);
    }

    protected override void VisitHeader(HeaderMarkup markup, ref StringBuilder state)
    {
        if (!IsSpoken) {
            base.VisitHeader(markup, ref state);
            return;
        }

        var start = state.Length;
        Visit(markup.Content, ref state);
        EndSentence(state, start);
    }

    protected override void VisitListItem(ListItemMarkup markup, ref StringBuilder state)
    {
        if (!IsSpoken) {
            base.VisitListItem(markup, ref state);
            return;
        }

        var start = state.Length;
        Visit(markup.Content, ref state);
        EndSentence(state, start);
    }

    protected override void VisitBlockQuote(BlockQuoteMarkup markup, ref StringBuilder state)
    {
        if (!IsSpoken) {
            base.VisitBlockQuote(markup, ref state);
            return;
        }

        var start = state.Length;
        Visit(markup.Content, ref state);
        EndSentence(state, start);
    }

    protected override void VisitTable(TableMarkup markup, ref StringBuilder state)
    {
        if (IsSpoken)
            return;

        if (ShowStyleTokens) {
            base.VisitTable(markup, ref state);
            return;
        }

        // The delimiter row is pure syntax, so flattened text (notifications, chat-list &
        // quote previews) leaves it out and keeps just the rows.
        VisitTableRow(markup.Header, ref state);
        foreach (var row in markup.Rows) {
            state.AppendLine();
            VisitTableRow(row, ref state);
        }
    }

    protected override void VisitStylized(StylizedMarkup markup, ref StringBuilder state)
    {
        if (markup.Style == TextStyle.Spoiler && IsSpoken)
            return;

        if (markup.Style == TextStyle.Spoiler && !ShowStyleTokens) {
            // No reveal affordance in flattened text (notifications, chat-list & quote previews),
            // so the content is replaced with a mask instead of leaking through.
            var inner = ActualLab.Text.StringBuilderExt.Acquire();
            Visit(markup.Content, ref inner);
            state.Append(StylizedMarkup.Mask(inner.ToStringAndRelease()));
            return;
        }

        if (ShowStyleTokens)
            state.Append(markup.StyleToken);
        Visit(markup.Content, ref state);
        if (ShowStyleTokens)
            state.Append(markup.StyleToken);
    }

    // Private methods

    private static void EndSentence(StringBuilder sb, int start)
    {
        var last = sb.Length - 1;
        while (last >= start && char.IsWhiteSpace(sb[last]))
            last--;
        if (last < start || SentenceEndChars.Contains(sb[last]))
            return;

        sb.Insert(last + 1, '.');
    }

    private static string FormatUrlForQuote(UrlMarkup markup)
        => markup.Kind is UrlMarkupKind.Www
            && markup.Url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
            ? "GIF"
            : markup.Format();
}
