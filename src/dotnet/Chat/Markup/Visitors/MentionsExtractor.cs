namespace ActualChat.Chat;

public record MentionsExtractor : RefStatelessMarkupVisitor<List<string>>
{
    public List<string> ExtractAuthorIds(Markup markup)
    {
        var mentions = new List<string>();
        Visit(markup, ref mentions);
        return mentions;
    }

    protected override void VisitStylized(StylizedMarkup markup, ref List<string> state)
    { }

    protected override void VisitUrl(UrlMarkup markup, ref List<string> state)
    { }

    protected override void VisitMention(MentionMarkup markup, ref List<string> state)
        => state.Add(markup.Id);

    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref List<string> state)
    { }

    protected override void VisitPlainText(PlainTextMarkup markup, ref List<string> state)
    { }

    protected override void VisitPlayableText(PlayableTextMarkup markup, ref List<string> state)
    { }

    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref List<string> state)
    { }

    protected override void VisitNewLine(NewLineMarkup markup, ref List<string> state)
    { }

    protected override void VisitUnparsed(UnparsedTextMarkup markup, ref List<string> state)
    { }
}
