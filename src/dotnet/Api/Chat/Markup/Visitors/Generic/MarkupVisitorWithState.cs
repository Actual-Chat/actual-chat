namespace ActualChat.Chat;

public abstract record MarkupVisitorWithState<TState, TResult>
{
    protected virtual TResult Visit(Markup markup, ref TState state)
        => markup switch {
            MarkupSeq markupSeq => VisitSeq(markupSeq, ref state),
            CodeBlockMarkup codeBlockMarkup => VisitCodeBlock(codeBlockMarkup, ref state),
            MentionMarkup mention => VisitMention(mention, ref state),
            UrlMarkup urlMarkup => VisitUrl(urlMarkup, ref state),
            StylizedMarkup stylizedMarkup => VisitStylized(stylizedMarkup, ref state),
            TextMarkup textMarkup => VisitText(textMarkup, ref state),
            _ => VisitUnknown(markup, ref state),
        };

    protected virtual TResult VisitText(TextMarkup markup, ref TState state)
        => markup switch {
            PlainTextMarkup plainTextMarkup => VisitPlainText(plainTextMarkup, ref state),
            PlayableTextMarkup playableTextMarkup => VisitPlayableText(playableTextMarkup, ref state),
            PreformattedTextMarkup preformattedTextMarkup => VisitPreformattedText(preformattedTextMarkup, ref state),
            NewLineMarkup newLineMarkup => VisitNewLine(newLineMarkup, ref state),
            UnparsedTextMarkup unparsedMarkup => VisitUnparsed(unparsedMarkup, ref state),
            _ => VisitUnknown(markup, ref state),
        };

    protected abstract TResult VisitSeq(MarkupSeq markup, ref TState state);
    protected abstract TResult VisitStylized(StylizedMarkup markup, ref TState state);

    protected abstract TResult VisitUrl(UrlMarkup markup, ref TState state);
    protected abstract TResult VisitMention(MentionMarkup markup, ref TState state);
    protected abstract TResult VisitCodeBlock(CodeBlockMarkup markup, ref TState state);

    protected abstract TResult VisitPlainText(PlainTextMarkup markup, ref TState state);
    protected abstract TResult VisitPlayableText(PlayableTextMarkup markup, ref TState state);
    protected abstract TResult VisitPreformattedText(PreformattedTextMarkup markup, ref TState state);
    protected abstract TResult VisitNewLine(NewLineMarkup markup, ref TState state);
    protected abstract TResult VisitUnparsed(UnparsedTextMarkup markup, ref TState state);

    protected virtual TResult VisitUnknown(Markup markup, ref TState state)
        => throw new ArgumentOutOfRangeException(nameof(markup));
}

public abstract record MarkupVisitorWithState<TState>
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
        case NewLineMarkup newLineMarkup:
            VisitNewLine(newLineMarkup, ref state);
            break;
        case UnparsedTextMarkup unparsedMarkup:
            VisitUnparsed(unparsedMarkup, ref state);
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
