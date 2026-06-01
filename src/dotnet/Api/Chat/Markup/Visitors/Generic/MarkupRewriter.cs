namespace ActualChat.Chat;

public abstract record MarkupRewriter<TState> : MarkupVisitorWithState<TState, Markup>
{
    protected override Markup VisitList(ListMarkup markup, ref TState state)
    {
        var newItems = new List<ListItemMarkup>();
        var isUnchanged = true;
        foreach (var item in markup.Items) {
            var newItem = VisitListItem(item, ref state);
            if (newItem is ListItemMarkup newListItem) {
                newItems.Add(newListItem);
                isUnchanged &= newItem == item;
            }
            else
                isUnchanged = false;
        }
        return isUnchanged ? markup : new ListMarkup(newItems);
    }

    protected override Markup VisitSeq(MarkupSeq markup, ref TState state)
    {
        var newItems = new List<Markup>();
        var isUnchanged = true;
        foreach (var item in markup.Items) {
            var newItem = Visit(item, ref state);
            if (newItem != null!)
                newItems.Add(newItem);
            isUnchanged &= newItem == item;
        }
        return isUnchanged ? markup
            : new MarkupSeq(newItems.ToArray());
    }

    protected override Markup VisitStylized(StylizedMarkup markup, ref TState state)
    {
        var newMarkup = Visit(markup.Content, ref state);
        return newMarkup == markup ? markup
            : new StylizedMarkup(newMarkup, markup.Style);
    }

    protected override Markup VisitParagraph(ParagraphMarkup markup, ref TState state)
    {
        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new ParagraphMarkup(newContent);
    }

    protected override Markup VisitHeader(HeaderMarkup markup, ref TState state)
    {
        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new HeaderMarkup(markup.Level, newContent);
    }

    protected override Markup VisitBlockQuote(BlockQuoteMarkup markup, ref TState state)
    {
        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new BlockQuoteMarkup(newContent);
    }

    protected override Markup VisitUrl(UrlMarkup markup, ref TState state) => markup;
    protected override Markup VisitMention(MentionMarkup markup, ref TState state) => markup;
    protected override Markup VisitCodeBlock(CodeBlockMarkup markup, ref TState state) => markup;

    protected override Markup VisitPlainText(PlainTextMarkup markup, ref TState state) => markup;
    protected override Markup VisitPlayableText(PlayableTextMarkup markup, ref TState state) => markup;
    protected override Markup VisitPreformattedText(PreformattedTextMarkup markup, ref TState state) => markup;
    protected override Markup VisitNewLine(NewLineMarkup markup, ref TState state) => markup;
    protected override Markup VisitUnparsed(UnparsedTextMarkup markup, ref TState state) => markup;

    protected override Markup VisitUnknown(Markup markup, ref TState state) => markup;
}
