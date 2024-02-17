namespace ActualChat.Chat;

public record MentionExtractor : MarkupVisitorWithState<HashSet<MentionId>>
{
    public HashSet<MentionId> GetMentionIds(Markup markup)
    {
        var mentions = new HashSet<MentionId>();
        Visit(markup, ref mentions);
        return mentions;
    }

    protected override void VisitMention(MentionMarkup markup, ref HashSet<MentionId> state)
        => state.Add(markup.Id);

    protected override void VisitStylized(StylizedMarkup markup, ref HashSet<MentionId> state)
        => Visit(markup.Content, ref state);

    protected override void VisitUrl(UrlMarkup markup, ref HashSet<MentionId> state) { }
    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref HashSet<MentionId> state) { }
    protected override void VisitPlainText(PlainTextMarkup markup, ref HashSet<MentionId> state) { }
    protected override void VisitPlayableText(PlayableTextMarkup markup, ref HashSet<MentionId> state) { }
    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref HashSet<MentionId> state) { }
    protected override void VisitNewLine(NewLineMarkup markup, ref HashSet<MentionId> state) { }
    protected override void VisitUnparsed(UnparsedTextMarkup markup, ref HashSet<MentionId> state) { }
    protected override void VisitUnknown(Markup markup, ref HashSet<MentionId> state) { }
}
