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
        Markup? prevItem = null;
        foreach (var item in markup.Items) {
            // NOTE: Add new line separator between block markups and between block and inline markups.
            if (prevItem is not null && (item.IsBlockMarkup() || prevItem.IsBlockMarkup()))
                state.Append(NewLineMarkup.Instance.Format());
            Visit(item, ref state);
            prevItem = item;
        }
    }

    protected override void VisitListItem(ListItemMarkup markup, ref StringBuilder state)
    {
        state.Append(markup.GetPrefix());
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
    bool ShowStyleTokens = true
    ) : MarkupFormatterBase
{
    public static readonly MarkupFormatter Default = new();
    public static readonly MarkupFormatter Readable = new(MentionMarkup.NameOrNotAvailableFormatter);
    public static readonly MarkupFormatter ReadableUnstyled = Readable with { ShowStyleTokens = false };

    public MarkupFormatter() : this(MentionMarkup.DefaultFormatter, true) { }
    public MarkupFormatter(bool showStyleTokens) : this(MentionMarkup.DefaultFormatter, showStyleTokens) { }

    // Protected methods

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
}
