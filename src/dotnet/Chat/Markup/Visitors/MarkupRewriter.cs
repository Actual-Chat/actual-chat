namespace ActualChat.Chat;

public abstract class MarkupRewriter : MarkupVisitor<Markup>
{
    public virtual Markup Rewrite(Markup markup)
        => Visit(markup);

    protected override Markup VisitSeq(MarkupSeq markup)
    {
        var newItems = new List<Markup>();
        var isUnchanged = true;
        foreach (var item in markup.Items) {
            var newItem = Visit(item);
            if (newItem != null!)
                newItems.Add(newItem);
            isUnchanged &= ReferenceEquals(newItem, item);
        }
        return isUnchanged ? markup : new MarkupSeq(newItems);
    }

    protected override Markup VisitStylized(StylizedMarkup markup)
    {
        var newMarkup = Visit(markup.Content);
        return ReferenceEquals(newMarkup, markup) ? markup : markup with { Content = newMarkup };
    }

    protected override Markup VisitUrl(UrlMarkup markup) => markup;
    protected override Markup VisitMention(MentionMarkup markup) => markup;
    protected override Markup VisitCodeBlock(CodeBlockMarkup markup) => markup;

    protected override Markup VisitPlainText(PlainTextMarkup markup) => markup;
    protected override Markup VisitPlayableText(PlayableTextMarkup markup) => markup;
    protected override Markup VisitPreformattedText(PreformattedTextMarkup markup) => markup;
    protected override Markup VisitNewLine(NewLineMarkup markup) => markup;
    protected override Markup VisitUnparsed(UnparsedTextMarkup markup) => markup;

    protected override Markup VisitUnknown(Markup markup) => markup;
}
