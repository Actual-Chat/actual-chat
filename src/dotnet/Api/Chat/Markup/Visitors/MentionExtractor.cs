namespace ActualChat.Chat;

public record MentionExtractor : MarkupVisitorWithState<HashSet<MentionRef>>
{
    public static MentionExtractor Instance { get; } = new();

    public HashSet<MentionRef> GetMentionIds(Markup markup)
    {
        var mentions = new HashSet<MentionRef>();
        Visit(markup, ref mentions);
        return mentions;
    }

    protected override void VisitListItem(ListItemMarkup markup, ref HashSet<MentionRef> state)
        => Visit(markup.Content, ref state);

    protected override void VisitTableCell(TableCellMarkup markup, ref HashSet<MentionRef> state)
        => Visit(markup.Content, ref state);

    protected override void VisitParagraph(ParagraphMarkup markup, ref HashSet<MentionRef> state)
        => Visit(markup.Content, ref state);

    protected override void VisitHeader(HeaderMarkup markup, ref HashSet<MentionRef> state)
        => Visit(markup.Content, ref state);

    protected override void VisitMention(MentionMarkup markup, ref HashSet<MentionRef> state)
        => state.Add(markup.Id);

    protected override void VisitStylized(StylizedMarkup markup, ref HashSet<MentionRef> state)
        => Visit(markup.Content, ref state);

    protected override void VisitUrl(UrlMarkup markup, ref HashSet<MentionRef> state) { }
    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref HashSet<MentionRef> state) { }
    protected override void VisitPlainText(PlainTextMarkup markup, ref HashSet<MentionRef> state) { }
    protected override void VisitPlayableText(PlayableTextMarkup markup, ref HashSet<MentionRef> state) { }
    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref HashSet<MentionRef> state) { }
    protected override void VisitNewLine(NewLineMarkup markup, ref HashSet<MentionRef> state) { }
    protected override void VisitUnparsed(UnparsedTextMarkup markup, ref HashSet<MentionRef> state) { }
    protected override void VisitHashtag(HashtagMarkup markup, ref HashSet<MentionRef> state) { }
    protected override void VisitUnknown(Markup markup, ref HashSet<MentionRef> state) { }
}
