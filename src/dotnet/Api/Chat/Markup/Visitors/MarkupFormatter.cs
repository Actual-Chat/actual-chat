using Cysharp.Text;

namespace ActualChat.Chat;

public interface IMarkupFormatter
{
    string Format(Markup markup);
    void FormatTo(Markup markup, ref Utf16ValueStringBuilder sb);
}

public abstract record MarkupFormatterBase : MarkupVisitorWithState<Utf16ValueStringBuilder>, IMarkupFormatter
{
    public string Format(Markup markup)
    {
        var sb = ZString.CreateStringBuilder();
        FormatTo(markup, ref sb);
        return sb.ToString();
    }

    public void FormatTo(Markup markup, ref Utf16ValueStringBuilder sb)
        => Visit(markup, ref sb);

    // Protected methods

    protected override void VisitStylized(StylizedMarkup markup, ref Utf16ValueStringBuilder state)
    {
        state.Append(markup.StyleToken);
        Visit(markup.Content, ref state);
        state.Append(markup.StyleToken);
    }

    protected override void VisitUrl(UrlMarkup markup, ref Utf16ValueStringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitMention(MentionMarkup markup, ref Utf16ValueStringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref Utf16ValueStringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitPlainText(PlainTextMarkup markup, ref Utf16ValueStringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitPlayableText(PlayableTextMarkup markup, ref Utf16ValueStringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref Utf16ValueStringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitNewLine(NewLineMarkup markup, ref Utf16ValueStringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitUnparsed(UnparsedTextMarkup markup, ref Utf16ValueStringBuilder state)
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

    protected override void VisitMention(MentionMarkup markup, ref Utf16ValueStringBuilder state)
        => state.Append(MentionFormatter.Invoke(markup));

    protected override void VisitStylized(StylizedMarkup markup, ref Utf16ValueStringBuilder state)
    {
        if (ShowStyleTokens)
            state.Append(markup.StyleToken);
        Visit(markup.Content, ref state);
        if (ShowStyleTokens)
            state.Append(markup.StyleToken);
    }
}
