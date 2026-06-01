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
            isUnchanged &= newItem == item;
        }
        return isUnchanged ? markup
            : new MarkupSeq(newItems.ToArray());
    }

    protected override async ValueTask<Markup> VisitList(ListMarkup markup, CancellationToken cancellationToken)
    {
        var newItems = new List<ListItemMarkup>();
        var isUnchanged = false;
        foreach (var item in markup.Items) {
            var newItem = await Visit(item, cancellationToken).ConfigureAwait(false);
            if (newItem is ListItemMarkup newListItem) {
                newItems.Add(newListItem);
                isUnchanged &= newItem == item;
            }
            else
                isUnchanged = false;
        }
        return isUnchanged ? markup
            : new ListMarkup(newItems);
    }

    protected override async ValueTask<Markup> VisitListItem(ListItemMarkup markup, CancellationToken cancellationToken)
    {
        var newMarkup = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return newMarkup == markup ? markup
            : new ListItemMarkup(newMarkup);
    }

    protected override async ValueTask<Markup> VisitStylized(StylizedMarkup markup, CancellationToken cancellationToken)
    {
        var newMarkup = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return newMarkup == markup ? markup
            : new StylizedMarkup(newMarkup, markup.Style);
    }

    protected override async ValueTask<Markup> VisitParagraph(ParagraphMarkup markup, CancellationToken cancellationToken)
    {
        var newContent = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return newContent == markup.Content ? markup : new ParagraphMarkup(newContent);
    }

    protected override async ValueTask<Markup> VisitHeader(HeaderMarkup markup, CancellationToken cancellationToken)
    {
        var newContent = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return newContent == markup.Content ? markup : new HeaderMarkup(markup.Level, newContent);
    }

    protected override async ValueTask<Markup> VisitBlockQuote(BlockQuoteMarkup markup, CancellationToken cancellationToken)
    {
        var newContent = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return newContent == markup.Content ? markup : new BlockQuoteMarkup(newContent);
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
