namespace ActualChat.Chat;

public record LinkExtractor : MarkupVisitorWithState<HashSet<string>>
{
    public HashSet<string> GetLinks(Markup markup)
    {
        var mentions = new HashSet<string>(StringComparer.Ordinal);
        Visit(markup, ref mentions);
        return mentions;
    }

    protected override void VisitMention(MentionMarkup markup, ref HashSet<string> state) { }

    protected override void VisitStylized(StylizedMarkup markup, ref HashSet<string> state)
        => Visit(markup.Content, ref state);

    protected override void VisitUrl(UrlMarkup markup, ref HashSet<string> state)
        => state.Add(markup.Url);
    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref HashSet<string> state) { }
    protected override void VisitPlainText(PlainTextMarkup markup, ref HashSet<string> state) { }
    protected override void VisitPlayableText(PlayableTextMarkup markup, ref HashSet<string> state) { }
    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref HashSet<string> state) { }
    protected override void VisitNewLine(NewLineMarkup markup, ref HashSet<string> state) { }
    protected override void VisitUnparsed(UnparsedTextMarkup markup, ref HashSet<string> state) { }
    protected override void VisitUnknown(Markup markup, ref HashSet<string> state) { }
}
