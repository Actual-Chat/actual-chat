namespace ActualChat.Chat;

public class MarkupValidator : MarkupVisitor<bool>
{
    public enum AggregationMode { All, Any }

    public static MarkupValidator ContainsMention { get; } = new(m => m is Mention, AggregationMode.Any);

    public static MarkupValidator ContainsAuthorMention(string authorId)
        => new (m
                => m is Mention {Kind: MentionKind.AuthorId} mention && OrdinalEquals(mention.Target, authorId),
            AggregationMode.Any);

    private readonly Func<Markup, bool> _predicate;
    private readonly AggregationMode _aggregationMode;

    public MarkupValidator(Func<Markup, bool> predicate, AggregationMode aggregationMode)
    {
        _predicate = predicate;
        _aggregationMode = aggregationMode;
    }

    public bool IsValid(Markup markup)
        => Visit(markup);

    protected override bool VisitSeq(MarkupSeq markup)
        => _aggregationMode == AggregationMode.All
            ? markup.Items.All(Visit)
            : markup.Items.Any(Visit);

    protected override bool VisitStylized(StylizedMarkup markup)
        => _aggregationMode == AggregationMode.All
            ? Visit(markup.Content) && _predicate(markup)
            : Visit(markup.Content) || _predicate(markup);

    protected override bool VisitUrl(UrlMarkup markup) => _predicate(markup);
    protected override bool VisitMention(Mention markup) => _predicate(markup);
    protected override bool VisitCodeBlock(CodeBlockMarkup markup) => _predicate(markup);
    protected override bool VisitPlainText(PlainTextMarkup markup) => _predicate(markup);
    protected override bool VisitPlayableText(PlayableTextMarkup markup) => _predicate(markup);
    protected override bool VisitPreformattedText(PreformattedTextMarkup markup) => _predicate(markup);
    protected override bool VisitUnparsed(UnparsedTextMarkup markup) => _predicate(markup);
    protected override bool VisitCustom(CustomMarkup markup) => _predicate(markup);
}
