namespace ActualChat.Chat;

public abstract record MarkupRewriter<TState> : MarkupVisitorWithState<TState, Markup>
{
    protected override Markup VisitSeq(MarkupSeq markup, ref TState state)
    {
        var newItems = new List<Markup>();
        var isUnchanged = true;
        foreach (var item in markup.Items) {
            var newItem = Visit(item, ref state);
            if (newItem != null!)
                newItems.Add(newItem);
            isUnchanged &= ReferenceEquals(newItem, item);
        }
        return isUnchanged ? markup : new MarkupSeq(newItems);
    }

    protected override Markup VisitStylized(StylizedMarkup markup, ref TState state)
    {
        var newMarkup = Visit(markup.Content, ref state);
        return ReferenceEquals(newMarkup, markup) ? markup : markup with { Content = newMarkup };
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
