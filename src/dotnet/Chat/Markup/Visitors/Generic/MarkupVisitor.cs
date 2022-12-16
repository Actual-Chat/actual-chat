namespace ActualChat.Chat;

public abstract record MarkupVisitor<TResult>
{
    protected virtual TResult Visit(Markup markup)
        => markup switch {
            MarkupSeq markupSeq => VisitSeq(markupSeq),
            CodeBlockMarkup codeBlockMarkup => VisitCodeBlock(codeBlockMarkup),
            MentionMarkup mention => VisitMention(mention),
            UrlMarkup urlMarkup => VisitUrl(urlMarkup),
            StylizedMarkup stylizedMarkup => VisitStylized(stylizedMarkup),
            TextMarkup textMarkup => VisitText(textMarkup),
            _ => VisitUnknown(markup),
        };

    protected virtual TResult VisitText(TextMarkup markup)
        => markup switch {
            PlainTextMarkup plainTextMarkup => VisitPlainText(plainTextMarkup),
            PlayableTextMarkup playableTextMarkup => VisitPlayableText(playableTextMarkup),
            PreformattedTextMarkup preformattedTextMarkup => VisitPreformattedText(preformattedTextMarkup),
            NewLineMarkup newLineMarkup => VisitNewLine(newLineMarkup),
            UnparsedTextMarkup unparsedMarkup => VisitUnparsed(unparsedMarkup),
            _ => VisitUnknown(markup),
        };

    protected abstract TResult VisitSeq(MarkupSeq markup);
    protected abstract TResult VisitStylized(StylizedMarkup markup);

    protected abstract TResult VisitUrl(UrlMarkup markup);
    protected abstract TResult VisitMention(MentionMarkup markup);
    protected abstract TResult VisitCodeBlock(CodeBlockMarkup markup);

    protected abstract TResult VisitPlainText(PlainTextMarkup markup);
    protected abstract TResult VisitPlayableText(PlayableTextMarkup markup);
    protected abstract TResult VisitPreformattedText(PreformattedTextMarkup markup);
    protected abstract TResult VisitNewLine(NewLineMarkup markup);
    protected abstract TResult VisitUnparsed(UnparsedTextMarkup markup);

    protected virtual TResult VisitUnknown(Markup markup)
        => throw new ArgumentOutOfRangeException(nameof(markup));
}
