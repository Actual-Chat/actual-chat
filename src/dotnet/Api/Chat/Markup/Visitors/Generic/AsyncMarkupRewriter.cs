namespace ActualChat.Chat;

public abstract record AsyncMarkupRewriter : AsyncMarkupVisitor<Markup>
{
    protected override async ValueTask<Markup> VisitSeq(MarkupSeq markup, CancellationToken cancellationToken)
    {
        var newItems = new List<Markup>();
        var isUnchanged = false;
        foreach (var item in markup.Items) {
            var newItem = await Visit(item, cancellationToken).ConfigureAwait(false);
            if (newItem != null!)
                newItems.Add(newItem);
            isUnchanged &= newItem == item;
        }

        return isUnchanged ? markup
            : new MarkupSeq(newItems.ToArray());
    }

    protected override async ValueTask<Markup> VisitList(ListMarkup markup, CancellationToken cancellationToken)
    {
        var newItems = new List<ListItemMarkup>();
        var isUnchanged = false;
        foreach (var item in markup.Items) {
            var newItem = await Visit(item, cancellationToken).ConfigureAwait(false);
            if (newItem is ListItemMarkup newListItem) {
                newItems.Add(newListItem);
                isUnchanged &= newItem == item;
            }
            else
                isUnchanged = false;
        }

        return isUnchanged ? markup
            : new ListMarkup(newItems);
    }

    protected override async ValueTask<Markup> VisitTable(TableMarkup markup, CancellationToken cancellationToken)
    {
        // A rewrite that drops a row or a cell would break the table's rectangular shape,
        // so anything but a row-for-row, cell-for-cell result leaves the table as it was.
        var header = await VisitTableRow(markup.Header, cancellationToken).ConfigureAwait(false);
        if (header is not TableRowMarkup newHeader)
            return markup;

        var isUnchanged = ReferenceEquals(newHeader, markup.Header);
        var newRows = new TableRowMarkup[markup.Rows.Length];
        for (var i = 0; i < markup.Rows.Length; i++) {
            var row = markup.Rows[i];
            if (await VisitTableRow(row, cancellationToken).ConfigureAwait(false) is not TableRowMarkup newRow)
                return markup;

            newRows[i] = newRow;
            isUnchanged &= ReferenceEquals(newRow, row);
        }

        return isUnchanged ? markup : new TableMarkup(newHeader, markup.Alignments, newRows);
    }

    protected override async ValueTask<Markup> VisitTableRow(TableRowMarkup markup, CancellationToken cancellationToken)
    {
        var newCells = new TableCellMarkup[markup.Cells.Length];
        var isUnchanged = true;
        for (var i = 0; i < markup.Cells.Length; i++) {
            var cell = markup.Cells[i];
            if (await VisitTableCell(cell, cancellationToken).ConfigureAwait(false) is not TableCellMarkup newCell)
                return markup;

            newCells[i] = newCell;
            isUnchanged &= ReferenceEquals(newCell, cell);
        }

        return isUnchanged ? markup : new TableRowMarkup(newCells);
    }

    protected override async ValueTask<Markup> VisitTableCell(
        TableCellMarkup markup,
        CancellationToken cancellationToken)
    {
        var newContent = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return newContent == markup.Content ? markup : new TableCellMarkup(newContent);
    }

    protected override async ValueTask<Markup> VisitListItem(ListItemMarkup markup, CancellationToken cancellationToken)
    {
        var newMarkup = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return newMarkup == markup ? markup
            : new ListItemMarkup(newMarkup);
    }

    protected override async ValueTask<Markup> VisitStylized(StylizedMarkup markup, CancellationToken cancellationToken)
    {
        var newMarkup = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return newMarkup == markup ? markup
            : new StylizedMarkup(newMarkup, markup.Style);
    }

    protected override async ValueTask<Markup> VisitParagraph(
        ParagraphMarkup markup,
        CancellationToken cancellationToken)
    {
        var newContent = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return newContent == markup.Content ? markup : new ParagraphMarkup(newContent);
    }

    protected override async ValueTask<Markup> VisitHeader(HeaderMarkup markup, CancellationToken cancellationToken)
    {
        var newContent = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return newContent == markup.Content ? markup : new HeaderMarkup(markup.Level, newContent);
    }

    protected override async ValueTask<Markup> VisitBlockQuote(
        BlockQuoteMarkup markup,
        CancellationToken cancellationToken)
    {
        var newContent = await Visit(markup.Content, cancellationToken).ConfigureAwait(false);
        return newContent == markup.Content ? markup : new BlockQuoteMarkup(newContent);
    }

    protected override ValueTask<Markup> VisitUrl(UrlMarkup markup, CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitMention(MentionMarkup markup, CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitCodeBlock(CodeBlockMarkup markup, CancellationToken cancellationToken)
        => new (markup);

    protected override ValueTask<Markup> VisitPlainText(PlainTextMarkup markup, CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitPlayableText(
        PlayableTextMarkup markup,
        CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitPreformattedText(
        PreformattedTextMarkup markup,
        CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitNewLine(NewLineMarkup markup, CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitUnparsed(UnparsedTextMarkup markup, CancellationToken cancellationToken)
        => new (markup);
    protected override ValueTask<Markup> VisitHashtag(HashtagMarkup markup, CancellationToken cancellationToken)
        => new (markup);
}
