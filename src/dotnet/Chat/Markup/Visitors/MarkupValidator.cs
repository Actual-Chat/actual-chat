namespace ActualChat.Chat;

public record MarkupValidator : MarkupVisitor<bool>
{
    public enum AggregationMode { All, Any }

    public static MarkupValidator ContainsAnyMention { get; } = new(m => m is MentionMarkup, AggregationMode.Any);

    public static MarkupValidator ContainsMention(MentionId id)
        => new(m => m is MentionMarkup mention && mention.Id == id, AggregationMode.Any);

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
    protected override bool VisitMention(MentionMarkup markup) => _predicate(markup);
    protected override bool VisitCodeBlock(CodeBlockMarkup markup) => _predicate(markup);

    protected override bool VisitPlainText(PlainTextMarkup markup) => _predicate(markup);
    protected override bool VisitPlayableText(PlayableTextMarkup markup) => _predicate(markup);
    protected override bool VisitPreformattedText(PreformattedTextMarkup markup) => _predicate(markup);
    protected override bool VisitNewLine(NewLineMarkup markup) => _predicate(markup);
    protected override bool VisitUnparsed(UnparsedTextMarkup markup) => _predicate(markup);

    protected override bool VisitUnknown(Markup markup) => _predicate(markup);
}
