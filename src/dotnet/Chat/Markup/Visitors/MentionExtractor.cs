namespace ActualChat.Chat;

public record MentionExtractor : RefStatelessMarkupVisitor<HashSet<AuthorId>>
{
    public HashSet<AuthorId> GetMentionedAuthorIds(Markup markup)
    {
        var mentions = new HashSet<AuthorId>();
        Visit(markup, ref mentions);
        return mentions;
    }

    protected override void VisitStylized(StylizedMarkup markup, ref HashSet<AuthorId> state)
    { }

    protected override void VisitUrl(UrlMarkup markup, ref HashSet<AuthorId> state)
    { }

    protected override void VisitMention(MentionMarkup markup, ref HashSet<AuthorId> state)
        => state.Add(new AuthorId(markup.Id));

    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref HashSet<AuthorId> state)
    { }

    protected override void VisitPlainText(PlainTextMarkup markup, ref HashSet<AuthorId> state)
    { }

    protected override void VisitPlayableText(PlayableTextMarkup markup, ref HashSet<AuthorId> state)
    { }

    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref HashSet<AuthorId> state)
    { }

    protected override void VisitNewLine(NewLineMarkup markup, ref HashSet<AuthorId> state)
    { }

    protected override void VisitUnparsed(UnparsedTextMarkup markup, ref HashSet<AuthorId> state)
    { }
}
