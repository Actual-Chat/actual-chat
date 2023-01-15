namespace ActualChat.Chat;

public abstract record AsyncMarkupVisitor<TResult>
{
    protected virtual ValueTask<TResult> Visit(Markup markup, CancellationToken cancellationToken)
        => markup switch {
            MarkupSeq markupSeq => VisitSeq(markupSeq, cancellationToken),
            CodeBlockMarkup codeBlockMarkup => VisitCodeBlock(codeBlockMarkup, cancellationToken),
            MentionMarkup mention => VisitMention(mention, cancellationToken),
            UrlMarkup urlMarkup => VisitUrl(urlMarkup, cancellationToken),
            StylizedMarkup stylizedMarkup => VisitStylized(stylizedMarkup, cancellationToken),
            TextMarkup textMarkup => VisitText(textMarkup, cancellationToken),
            _ => VisitUnknown(markup, cancellationToken),
        };

    protected virtual ValueTask<TResult> VisitText(TextMarkup markup, CancellationToken cancellationToken)
        => markup switch {
            PlainTextMarkup plainTextMarkup => VisitPlainText(plainTextMarkup, cancellationToken),
            PlayableTextMarkup playableTextMarkup => VisitPlayableText(playableTextMarkup, cancellationToken),
            PreformattedTextMarkup preformattedTextMarkup => VisitPreformattedText(preformattedTextMarkup, cancellationToken),
            NewLineMarkup newLineMarkup => VisitNewLine(newLineMarkup, cancellationToken),
            UnparsedTextMarkup unparsedMarkup => VisitUnparsed(unparsedMarkup, cancellationToken),
            _ => VisitUnknown(markup, cancellationToken),
        };

    protected abstract ValueTask<TResult> VisitSeq(MarkupSeq markup, CancellationToken cancellationToken);
    protected abstract ValueTask<TResult> VisitStylized(StylizedMarkup markup, CancellationToken cancellationToken);

    protected abstract ValueTask<TResult> VisitUrl(UrlMarkup markup, CancellationToken cancellationToken);
    protected abstract ValueTask<TResult> VisitMention(MentionMarkup markup, CancellationToken cancellationToken);
    protected abstract ValueTask<TResult> VisitCodeBlock(CodeBlockMarkup markup, CancellationToken cancellationToken);

    protected abstract ValueTask<TResult> VisitPlainText(PlainTextMarkup markup, CancellationToken cancellationToken);
    protected abstract ValueTask<TResult> VisitPlayableText(PlayableTextMarkup markup, CancellationToken cancellationToken);
    protected abstract ValueTask<TResult> VisitPreformattedText(PreformattedTextMarkup markup, CancellationToken cancellationToken);
    protected abstract ValueTask<TResult> VisitNewLine(NewLineMarkup markup, CancellationToken cancellationToken);
    protected abstract ValueTask<TResult> VisitUnparsed(UnparsedTextMarkup markup, CancellationToken cancellationToken);

    protected virtual ValueTask<TResult> VisitUnknown(Markup markup, CancellationToken cancellationToken)
        => throw new ArgumentOutOfRangeException(nameof(markup));
}
