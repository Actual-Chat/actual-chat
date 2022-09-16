using Cysharp.Text;

namespace ActualChat.Chat;

public abstract record MarkupFormatterBase : RefStatelessMarkupVisitor<Utf16ValueStringBuilder>
{
    public string Format(Markup markup)
    {
        var sb = ZString.CreateStringBuilder();
        Format(markup, ref sb);
        return sb.ToString();
    }

    public void Format(Markup markup, ref Utf16ValueStringBuilder sb)
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

    protected override void VisitMention(Mention markup, ref Utf16ValueStringBuilder state)
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
    Func<Mention, string> MentionFormatter,
    bool ShowStyleTokens = true
    ) : MarkupFormatterBase
{
    public static MarkupFormatter Default { get; } = new();
    public static MarkupFormatter Readable { get; } = new(Mention.NameOrNotAvailableFormatter);
    public static MarkupFormatter ReadableUnstyled { get; } = Readable with { ShowStyleTokens = false };

    public MarkupFormatter() : this(Mention.DefaultFormatter, true) { }
    public MarkupFormatter(bool showStyleTokens) : this(Mention.DefaultFormatter, showStyleTokens) { }

    // Protected methods

    protected override void VisitMention(Mention markup, ref Utf16ValueStringBuilder state)
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
