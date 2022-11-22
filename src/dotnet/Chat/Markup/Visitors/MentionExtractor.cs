namespace ActualChat.Chat;

public record MentionExtractor : RefStatelessMarkupVisitor<HashSet<Symbol>>
{
    public HashSet<Symbol> GetMentionIds(Markup markup)
    {
        var mentions = new HashSet<Symbol>();
        Visit(markup, ref mentions);
        return mentions;
    }

    protected override void VisitStylized(StylizedMarkup markup, ref HashSet<Symbol> state)
    { }

    protected override void VisitUrl(UrlMarkup markup, ref HashSet<Symbol> state)
    { }

    protected override void VisitMention(MentionMarkup markup, ref HashSet<Symbol> state)
        => state.Add(markup.Id);

    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref HashSet<Symbol> state)
    { }

    protected override void VisitPlainText(PlainTextMarkup markup, ref HashSet<Symbol> state)
    { }

    protected override void VisitPlayableText(PlayableTextMarkup markup, ref HashSet<Symbol> state)
    { }

    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref HashSet<Symbol> state)
    { }

    protected override void VisitNewLine(NewLineMarkup markup, ref HashSet<Symbol> state)
    { }

    protected override void VisitUnparsed(UnparsedTextMarkup markup, ref HashSet<Symbol> state)
    { }
}
