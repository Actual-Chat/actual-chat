namespace ActualChat.Chat;

public abstract record StatelessMarkupVisitor<TState>
    where TState : class
{
    protected virtual void Visit(Markup markup, TState state)
    {
        switch (markup) {
        case MarkupSeq markupSeq:
            VisitSeq(markupSeq, state);
            break;
        case CodeBlockMarkup codeBlockMarkup:
            VisitCodeBlock(codeBlockMarkup, state);
            break;
        case MentionMarkup mention:
            VisitMention(mention, state);
            break;
        case UrlMarkup urlMarkup:
            VisitUrl(urlMarkup, state);
            break;
        case StylizedMarkup stylizedMarkup:
            VisitStylized(stylizedMarkup, state);
            break;
        case TextMarkup textMarkup:
            VisitText(textMarkup, state);
            break;
        default:
            VisitUnknown(markup, state);
            break;
        }
    }

    protected virtual void VisitText(TextMarkup markup, TState state)
    {
        switch (markup) {
        case PlainTextMarkup plainTextMarkup:
            VisitPlainText(plainTextMarkup, state);
            break;
        case PlayableTextMarkup playableTextMarkup:
            VisitPlayableText(playableTextMarkup, state);
            break;
        case PreformattedTextMarkup preformattedTextMarkup:
            VisitPreformattedText(preformattedTextMarkup, state);
            break;
        case UnparsedTextMarkup unparsedMarkup:
            VisitUnparsed(unparsedMarkup, state);
            break;
        case NewLineMarkup newLineMarkup:
            VisitNewLine(newLineMarkup, state);
            break;
        default:
            VisitUnknown(markup, state);
            break;
        }
    }

    protected virtual void VisitSeq(MarkupSeq markup, TState state)
    {
        foreach (var item in markup.Items)
            Visit(item, state);
    }

    protected abstract void VisitStylized(StylizedMarkup markup, TState state);

    protected abstract void VisitUrl(UrlMarkup markup, TState state);
    protected abstract void VisitMention(MentionMarkup markup, TState state);
    protected abstract void VisitCodeBlock(CodeBlockMarkup markup, TState state);

    protected abstract void VisitPlainText(PlainTextMarkup markup, TState state);
    protected abstract void VisitPlayableText(PlayableTextMarkup markup, TState state);
    protected abstract void VisitPreformattedText(PreformattedTextMarkup markup, TState state);
    protected abstract void VisitNewLine(NewLineMarkup markup, TState state);
    protected abstract void VisitUnparsed(UnparsedTextMarkup markup, TState state);

    protected virtual void VisitUnknown(Markup markup, TState state)
        => throw new ArgumentOutOfRangeException(nameof(markup));
}
