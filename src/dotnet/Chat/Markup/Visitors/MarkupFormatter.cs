using Cysharp.Text;

namespace ActualChat.Chat;

public record MarkupFormatter : RefStatelessMarkupVisitor<Utf16ValueStringBuilder>
{
    public static MarkupFormatter Default { get; } = new();
    public static MarkupFormatter Readable { get; } = new() { MentionFormat = MentionFormat.NameOnly };
    public static MarkupFormatter ReadableUnstyled { get; } = Readable with { ShowStyleTokens = false };

    public MentionFormat MentionFormat { get; init; } = MentionFormat.Full;
    public bool ShowStyleTokens { get; init; } = true;

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
        if (ShowStyleTokens)
            state.Append(markup.StyleToken);
        Visit(markup.Content, ref state);
        if (ShowStyleTokens)
            state.Append(markup.StyleToken);
    }

    protected override void VisitUrl(UrlMarkup markup, ref Utf16ValueStringBuilder state)
        => state.Append(markup.Format());

    protected override void VisitMention(Mention markup, ref Utf16ValueStringBuilder state)
        => state.Append(markup.Format(MentionFormat));

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
