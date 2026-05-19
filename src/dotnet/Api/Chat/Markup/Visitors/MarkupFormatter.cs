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

    public MarkupFormatter() : this(MentionMarkup.DefaultFormatter, true) { }
    public MarkupFormatter(bool showStyleTokens) : this(MentionMarkup.DefaultFormatter, showStyleTokens) { }

    // Protected methods

    protected override void VisitUrl(UrlMarkup markup, ref StringBuilder state)
        => state.Append(UrlFormatter?.Invoke(markup) ?? markup.Format());

    protected override void VisitMention(MentionMarkup markup, ref StringBuilder state)
        => state.Append(MentionFormatter.Invoke(markup));

    protected override void VisitStylized(StylizedMarkup markup, ref StringBuilder state)
    {
        if (ShowStyleTokens)
            state.Append(markup.StyleToken);
        Visit(markup.Content, ref state);
        if (ShowStyleTokens)
            state.Append(markup.StyleToken);
    }

    // Private methods

    private static string FormatUrlForQuote(UrlMarkup markup)
        => markup.Kind is UrlMarkupKind.Www
            && markup.Url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
            ? "GIF"
            : markup.Format();
}
