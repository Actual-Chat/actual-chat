namespace ActualChat.Chat;

/// <summary>
/// Collects every <see cref="HashtagMarkup"/> tag in a markup tree, lowercased —
/// hashtag matching is case-insensitive everywhere.
/// </summary>
public sealed record HashtagExtractor : MarkupVisitorWithState<HashSet<string>>
{
    public static HashtagExtractor Instance { get; } = new();

    public HashSet<string> GetTags(Markup markup)
    {
        var tags = new HashSet<string>();
        Visit(markup, ref tags);
        return tags;
    }

    protected override void VisitListItem(ListItemMarkup markup, ref HashSet<string> state)
        => Visit(markup.Content, ref state);

    protected override void VisitTableCell(TableCellMarkup markup, ref HashSet<string> state)
        => Visit(markup.Content, ref state);

    protected override void VisitParagraph(ParagraphMarkup markup, ref HashSet<string> state)
        => Visit(markup.Content, ref state);

    protected override void VisitHeader(HeaderMarkup markup, ref HashSet<string> state)
        => Visit(markup.Content, ref state);

    protected override void VisitHashtag(HashtagMarkup markup, ref HashSet<string> state)
        => state.Add(markup.Tag.ToLower());

    protected override void VisitStylized(StylizedMarkup markup, ref HashSet<string> state)
        => Visit(markup.Content, ref state);

    protected override void VisitMention(MentionMarkup markup, ref HashSet<string> state) { }
    protected override void VisitUrl(UrlMarkup markup, ref HashSet<string> state) { }
    protected override void VisitCodeBlock(CodeBlockMarkup markup, ref HashSet<string> state) { }
    protected override void VisitPlainText(PlainTextMarkup markup, ref HashSet<string> state) { }
    protected override void VisitPlayableText(PlayableTextMarkup markup, ref HashSet<string> state) { }
    protected override void VisitPreformattedText(PreformattedTextMarkup markup, ref HashSet<string> state) { }
    protected override void VisitNewLine(NewLineMarkup markup, ref HashSet<string> state) { }
    protected override void VisitUnparsed(UnparsedTextMarkup markup, ref HashSet<string> state) { }
    protected override void VisitUnknown(Markup markup, ref HashSet<string> state) { }
}
