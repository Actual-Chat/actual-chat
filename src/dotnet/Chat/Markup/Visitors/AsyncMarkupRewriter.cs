namespace ActualChat.Chat;

public abstract class AsyncMarkupRewriter : AsyncMarkupVisitor<Markup>
{
    public ValueTask<Markup> Rewrite(Markup markup, CancellationToken cancellationToken)
        => Visit(markup, cancellationToken);

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
        => ValueTask.FromResult<Markup>(markup);
    protected override ValueTask<Markup> VisitMention(MentionMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult<Markup>(markup);
    protected override ValueTask<Markup> VisitCodeBlock(CodeBlockMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult<Markup>(markup);

    protected override ValueTask<Markup> VisitPlainText(PlainTextMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult<Markup>(markup);
    protected override ValueTask<Markup> VisitPlayableText(PlayableTextMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult<Markup>(markup);
    protected override ValueTask<Markup> VisitPreformattedText(PreformattedTextMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult<Markup>(markup);
    protected override ValueTask<Markup> VisitNewLine(NewLineMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult<Markup>(markup);
    protected override ValueTask<Markup> VisitUnparsed(UnparsedTextMarkup markup, CancellationToken cancellationToken)
        => ValueTask.FromResult<Markup>(markup);
}
