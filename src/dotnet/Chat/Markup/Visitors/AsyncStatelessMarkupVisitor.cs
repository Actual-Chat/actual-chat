namespace ActualChat.Chat;

public abstract record AsyncStatelessMarkupVisitor<TState>
    where TState : class
{
    protected virtual ValueTask Visit(Markup markup, TState state, CancellationToken cancellationToken)
        => markup switch {
            MarkupSeq markupSeq => VisitSeq(markupSeq, state, cancellationToken),
            CodeBlockMarkup codeBlockMarkup => VisitCodeBlock(codeBlockMarkup, state, cancellationToken),
            MentionMarkup mention => VisitMention(mention, state, cancellationToken),
            UrlMarkup urlMarkup => VisitUrl(urlMarkup, state, cancellationToken),
            StylizedMarkup stylizedMarkup => VisitStylized(stylizedMarkup, state, cancellationToken),
            TextMarkup textMarkup => VisitText(textMarkup, state, cancellationToken),
            _ => VisitUnknown(markup, state, cancellationToken),
        };

    protected virtual ValueTask VisitText(TextMarkup markup, TState state, CancellationToken cancellationToken)
        => markup switch {
            PlainTextMarkup plainTextMarkup => VisitPlainText(plainTextMarkup, state, cancellationToken),
            PlayableTextMarkup playableTextMarkup => VisitPlayableText(playableTextMarkup, state, cancellationToken),
            PreformattedTextMarkup preformattedTextMarkup => VisitPreformattedText(preformattedTextMarkup, state, cancellationToken),
            NewLineMarkup newLineMarkup => VisitNewLine(newLineMarkup, state, cancellationToken),
            UnparsedTextMarkup unparsedMarkup => VisitUnparsed(unparsedMarkup, state, cancellationToken),
            _ => VisitUnknown(markup, state, cancellationToken),
        };

    protected abstract ValueTask VisitSeq(MarkupSeq markup, TState state, CancellationToken cancellationToken);
    protected abstract ValueTask VisitStylized(StylizedMarkup markup, TState state, CancellationToken cancellationToken);

    protected abstract ValueTask VisitUrl(UrlMarkup markup, TState state, CancellationToken cancellationToken);
    protected abstract ValueTask VisitMention(MentionMarkup markup, TState state, CancellationToken cancellationToken);
    protected abstract ValueTask VisitCodeBlock(CodeBlockMarkup markup, TState state, CancellationToken cancellationToken);

    protected abstract ValueTask VisitPlainText(PlainTextMarkup markup, TState state, CancellationToken cancellationToken);
    protected abstract ValueTask VisitPlayableText(PlayableTextMarkup markup, TState state, CancellationToken cancellationToken);
    protected abstract ValueTask VisitPreformattedText(PreformattedTextMarkup markup, TState state, CancellationToken cancellationToken);
    protected abstract ValueTask VisitNewLine(NewLineMarkup markup, TState state, CancellationToken cancellationToken);
    protected abstract ValueTask VisitUnparsed(UnparsedTextMarkup markup, TState state, CancellationToken cancellationToken);

    protected virtual ValueTask VisitUnknown(Markup markup, TState state, CancellationToken cancellationToken)
        => throw new ArgumentOutOfRangeException(nameof(markup));
}
