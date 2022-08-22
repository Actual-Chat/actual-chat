namespace ActualChat.Chat;

public abstract class MarkupRewriter : MarkupVisitor<Markup>
{
    public Markup Rewrite(Markup markup)
        => Visit(markup);

    protected override Markup VisitSeq(MarkupSeq markup)
    {
        var newItems = new List<Markup>();
        var isUnchanged = false;
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
    protected override Markup VisitMention(Mention markup) => markup;
    protected override Markup VisitCodeBlock(CodeBlockMarkup markup) => markup;
    protected override Markup VisitPlainText(PlainTextMarkup markup) => markup;
    protected override Markup VisitNewLine(NewLineMarkup markup) => markup;
    protected override Markup VisitPlayableText(PlayableTextMarkup markup) => markup;
    protected override Markup VisitPreformattedText(PreformattedTextMarkup markup) => markup;
    protected override Markup VisitUnparsed(UnparsedTextMarkup markup) => markup;
    protected override Markup VisitCustom(CustomMarkup markup) => markup;
}
