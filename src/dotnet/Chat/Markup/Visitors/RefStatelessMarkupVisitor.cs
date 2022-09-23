namespace ActualChat.Chat;

public abstract record RefStatelessMarkupVisitor<TState>
{
    protected virtual void Visit(Markup markup, ref TState state)
    {
        switch (markup) {
        case MarkupSeq markupSeq:
            VisitSeq(markupSeq, ref state);
            break;
        case CodeBlockMarkup codeBlockMarkup:
            VisitCodeBlock(codeBlockMarkup, ref state);
            break;
        case MentionMarkup mention:
            VisitMention(mention, ref state);
            break;
        case UrlMarkup urlMarkup:
            VisitUrl(urlMarkup, ref state);
            break;
        case StylizedMarkup stylizedMarkup:
            VisitStylized(stylizedMarkup, ref state);
            break;
        case TextMarkup textMarkup:
            VisitText(textMarkup, ref state);
            break;
        default:
            VisitUnknown(markup, ref state);
            break;
        }
    }

    protected virtual void VisitText(TextMarkup markup, ref TState state)
    {
        switch (markup) {
        case PlainTextMarkup plainTextMarkup:
            VisitPlainText(plainTextMarkup, ref state);
            break;
        case PlayableTextMarkup playableTextMarkup:
            VisitPlayableText(playableTextMarkup, ref state);
            break;
        case PreformattedTextMarkup preformattedTextMarkup:
            VisitPreformattedText(preformattedTextMarkup, ref state);
            break;
        case UnparsedTextMarkup unparsedMarkup:
            VisitUnparsed(unparsedMarkup, ref state);
            break;
        case NewLineMarkup newLineMarkup:
            VisitNewLine(newLineMarkup, ref state);
            break;
        default:
            VisitUnknown(markup, ref state);
            break;
        }
    }

    protected virtual void VisitSeq(MarkupSeq markup, ref TState state)
    {
        foreach (var item in markup.Items)
            Visit(item, ref state);
    }

    protected abstract void VisitStylized(StylizedMarkup markup, ref TState state);

    protected abstract void VisitUrl(UrlMarkup markup, ref TState state);
    protected abstract void VisitMention(MentionMarkup markup, ref TState state);
    protected abstract void VisitCodeBlock(CodeBlockMarkup markup, ref TState state);

    protected abstract void VisitPlainText(PlainTextMarkup markup, ref TState state);
    protected abstract void VisitPlayableText(PlayableTextMarkup markup, ref TState state);
    protected abstract void VisitPreformattedText(PreformattedTextMarkup markup, ref TState state);
    protected abstract void VisitNewLine(NewLineMarkup markup, ref TState state);
    protected abstract void VisitUnparsed(UnparsedTextMarkup markup, ref TState state);

    protected virtual void VisitUnknown(Markup markup, ref TState state)
        => throw new ArgumentOutOfRangeException(nameof(markup));
}
