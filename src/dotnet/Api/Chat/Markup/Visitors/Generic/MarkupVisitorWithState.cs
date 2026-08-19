namespace ActualChat.Chat;

public abstract record MarkupVisitorWithState<TState, TResult>
{
    protected virtual TResult Visit(Markup markup, ref TState state)
        => markup switch {
            MarkupSeq markupSeq => VisitSeq(markupSeq, ref state),
            ParagraphMarkup paragraphMarkup => VisitParagraph(paragraphMarkup, ref state),
            HeaderMarkup headerMarkup => VisitHeader(headerMarkup, ref state),
            BlockQuoteMarkup blockQuoteMarkup => VisitBlockQuote(blockQuoteMarkup, ref state),
            CodeBlockMarkup codeBlockMarkup => VisitCodeBlock(codeBlockMarkup, ref state),
            MentionMarkup mention => VisitMention(mention, ref state),
            UrlMarkup urlMarkup => VisitUrl(urlMarkup, ref state),
            StylizedMarkup stylizedMarkup => VisitStylized(stylizedMarkup, ref state),
            TextMarkup textMarkup => VisitText(textMarkup, ref state),
            ListMarkup listMarkup => VisitList(listMarkup, ref state),
            ListItemMarkup listItemMarkup => VisitListItem(listItemMarkup, ref state),
            TableMarkup tableMarkup => VisitTable(tableMarkup, ref state),
            TableRowMarkup tableRowMarkup => VisitTableRow(tableRowMarkup, ref state),
            TableCellMarkup tableCellMarkup => VisitTableCell(tableCellMarkup, ref state),
            _ => VisitUnknown(markup, ref state),
        };

    protected virtual TResult VisitText(TextMarkup markup, ref TState state)
        => markup switch {
            PlainTextMarkup plainTextMarkup => VisitPlainText(plainTextMarkup, ref state),
            PlayableTextMarkup playableTextMarkup => VisitPlayableText(playableTextMarkup, ref state),
            PreformattedTextMarkup preformattedTextMarkup => VisitPreformattedText(preformattedTextMarkup, ref state),
            NewLineMarkup newLineMarkup => VisitNewLine(newLineMarkup, ref state),
            UnparsedTextMarkup unparsedMarkup => VisitUnparsed(unparsedMarkup, ref state),
            HashtagMarkup hashtagMarkup => VisitHashtag(hashtagMarkup, ref state),
            _ => VisitUnknown(markup, ref state),
        };

    protected abstract TResult VisitList(ListMarkup markup, ref TState state);
    protected abstract TResult VisitListItem(ListItemMarkup markup, ref TState state);
    protected abstract TResult VisitTable(TableMarkup markup, ref TState state);
    protected abstract TResult VisitTableRow(TableRowMarkup markup, ref TState state);
    protected abstract TResult VisitTableCell(TableCellMarkup markup, ref TState state);
    protected abstract TResult VisitParagraph(ParagraphMarkup markup, ref TState state);
    protected abstract TResult VisitHeader(HeaderMarkup markup, ref TState state);
    protected virtual TResult VisitBlockQuote(BlockQuoteMarkup markup, ref TState state)
        => Visit(markup.Content, ref state);

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
    protected abstract TResult VisitHashtag(HashtagMarkup markup, ref TState state);

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
        case ParagraphMarkup paragraphMarkup:
            VisitParagraph(paragraphMarkup, ref state);
            break;
        case HeaderMarkup headerMarkup:
            VisitHeader(headerMarkup, ref state);
            break;
        case BlockQuoteMarkup blockQuoteMarkup:
            VisitBlockQuote(blockQuoteMarkup, ref state);
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
        case ListMarkup listMarkup:
            VisitList(listMarkup, ref state);
            break;
        case ListItemMarkup listItemMarkup:
            VisitListItem(listItemMarkup, ref state);
            break;
        case TableMarkup tableMarkup:
            VisitTable(tableMarkup, ref state);
            break;
        case TableRowMarkup tableRowMarkup:
            VisitTableRow(tableRowMarkup, ref state);
            break;
        case TableCellMarkup tableCellMarkup:
            VisitTableCell(tableCellMarkup, ref state);
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
        case HashtagMarkup hashtagMarkup:
            VisitHashtag(hashtagMarkup, ref state);
            break;
        default:
            VisitUnknown(markup, ref state);
            break;
        }
    }

    protected virtual void VisitList(ListMarkup markup, ref TState state)
    {
        foreach (var item in markup.Items)
            VisitListItem(item, ref state);
    }

    protected virtual void VisitSeq(MarkupSeq markup, ref TState state)
    {
        foreach (var item in markup.Items)
            Visit(item, ref state);
    }

    protected virtual void VisitTable(TableMarkup markup, ref TState state)
    {
        VisitTableRow(markup.Header, ref state);
        foreach (var row in markup.Rows)
            VisitTableRow(row, ref state);
    }

    protected virtual void VisitTableRow(TableRowMarkup markup, ref TState state)
    {
        foreach (var cell in markup.Cells)
            VisitTableCell(cell, ref state);
    }

    protected abstract void VisitTableCell(TableCellMarkup markup, ref TState state);
    protected abstract void VisitListItem(ListItemMarkup markup, ref TState state);
    protected abstract void VisitParagraph(ParagraphMarkup markup, ref TState state);
    protected abstract void VisitHeader(HeaderMarkup markup, ref TState state);
    protected virtual void VisitBlockQuote(BlockQuoteMarkup markup, ref TState state)
        => Visit(markup.Content, ref state);
    protected abstract void VisitStylized(StylizedMarkup markup, ref TState state);

    protected abstract void VisitUrl(UrlMarkup markup, ref TState state);
    protected abstract void VisitMention(MentionMarkup markup, ref TState state);
    protected abstract void VisitCodeBlock(CodeBlockMarkup markup, ref TState state);

    protected abstract void VisitPlainText(PlainTextMarkup markup, ref TState state);
    protected abstract void VisitPlayableText(PlayableTextMarkup markup, ref TState state);
    protected abstract void VisitPreformattedText(PreformattedTextMarkup markup, ref TState state);
    protected abstract void VisitNewLine(NewLineMarkup markup, ref TState state);
    protected abstract void VisitUnparsed(UnparsedTextMarkup markup, ref TState state);
    protected abstract void VisitHashtag(HashtagMarkup markup, ref TState state);

    protected virtual void VisitUnknown(Markup markup, ref TState state)
        => throw new ArgumentOutOfRangeException(nameof(markup));
}
