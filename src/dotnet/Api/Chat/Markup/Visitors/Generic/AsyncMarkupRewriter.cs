namespace ActualChat.Chat;

public abstract record AsyncMarkupRewriter : AsyncMarkupVisitor<Markup>
{
    protected override async ValueTask<Markup> VisitSeq(MarkupSeq markup, CancellationToken cancellationToken)
    {
        var newItems = new List<Markup>();
        var isUnchanged = false;
        foreach (var item in markup.Items) {
            var newItem = await Visit(item, cancellationToken).ConfigureAwait(false);
            if (newItem != null!)
                newItems.Add(newItem);
            isUnchanged &= ReferenceEquals(newItem, item);
        }
        return isUnchanged ? markup : new MarkupSeq(newItems);
    }

    protected override async ValueTask<Markup> VisitStylized(StylizedMarkup markup, CancellationToken cancellationToken)
    {
        var newMarkup = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return ReferenceEquals(newMarkup, markup) ? markup : markup with { Content = newMarkup };
    }

    protected override ValueTask<Markup> VisitUrl(UrlMarkup markup, CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitMention(MentionMarkup markup, CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitCodeBlock(CodeBlockMarkup markup, CancellationToken cancellationToken)
        => new (markup);

    protected override ValueTask<Markup> VisitPlainText(PlainTextMarkup markup, CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitPlayableText(PlayableTextMarkup markup, CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitPreformattedText(PreformattedTextMarkup markup, CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitNewLine(NewLineMarkup markup, CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitUnparsed(UnparsedTextMarkup markup, CancellationToken cancellationToken)
        => new (markup);
}
