namespace ActualChat.Chat;

public abstract class MarkupVisitor
{
    protected virtual void Visit(Markup markup)
    {
        switch (markup) {
        case MarkupSeq markupSeq:
            VisitSeq(markupSeq);
            return;
        case CodeBlockMarkup codeBlockMarkup:
            VisitCodeBlock(codeBlockMarkup);
            return;
        case MentionMarkup mention:
            VisitMention(mention);
            return;
        case UrlMarkup urlMarkup:
            VisitUrl(urlMarkup);
            return;
        case StylizedMarkup stylizedMarkup:
            VisitStylized(stylizedMarkup);
            return;
        case TextMarkup textMarkup:
            VisitText(textMarkup);
            return;
        default:
            VisitUnknown(markup);
            return;
        }
    }

    protected virtual void VisitText(TextMarkup markup)
    {
        switch (markup) {
        case PlainTextMarkup plainTextMarkup:
            VisitPlainText(plainTextMarkup);
            return;
        case PlayableTextMarkup playableTextMarkup:
            VisitPlayableText(playableTextMarkup);
            return;
        case PreformattedTextMarkup preformattedTextMarkup:
            VisitPreformattedText(preformattedTextMarkup);
            return;
        case NewLineMarkup newLineMarkup:
            VisitNewLine(newLineMarkup);
            return;
        case UnparsedTextMarkup unparsedMarkup:
            VisitUnparsed(unparsedMarkup);
            return;
        default:
            VisitUnknown(markup);
            return;
        }
    }

    protected abstract void VisitSeq(MarkupSeq markup);
    protected abstract void VisitStylized(StylizedMarkup markup);

    protected abstract void VisitUrl(UrlMarkup markup);
    protected abstract void VisitMention(MentionMarkup markup);
    protected abstract void VisitCodeBlock(CodeBlockMarkup markup);

    protected abstract void VisitPlainText(PlainTextMarkup markup);
    protected abstract void VisitPlayableText(PlayableTextMarkup markup);
    protected abstract void VisitPreformattedText(PreformattedTextMarkup markup);
    protected abstract void VisitNewLine(NewLineMarkup markup);
    protected abstract void VisitUnparsed(UnparsedTextMarkup markup);

    protected virtual void VisitUnknown(Markup markup)
        => throw new ArgumentOutOfRangeException(nameof(markup));
}

public abstract class MarkupVisitor<TResult>
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
