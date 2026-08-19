namespace ActualChat.Chat;

public abstract record MarkupRewriter<TState> : MarkupVisitorWithState<TState, Markup>
{
    protected override Markup VisitList(ListMarkup markup, ref TState state)
    {
        var newItems = new List<ListItemMarkup>();
        var isUnchanged = true;
        foreach (var item in markup.Items) {
            var newItem = VisitListItem(item, ref state);
            if (newItem is ListItemMarkup newListItem) {
                newItems.Add(newListItem);
                isUnchanged &= newItem == item;
            }
            else
                isUnchanged = false;
        }

        return isUnchanged ? markup : new ListMarkup(newItems);
    }

    protected override Markup VisitTable(TableMarkup markup, ref TState state)
    {
        // A rewrite that drops a row or a cell would break the table's rectangular shape,
        // so anything but a row-for-row, cell-for-cell result leaves the table as it was.
        if (VisitTableRow(markup.Header, ref state) is not TableRowMarkup newHeader)
            return markup;

        var isUnchanged = ReferenceEquals(newHeader, markup.Header);
        var newRows = new TableRowMarkup[markup.Rows.Length];
        for (var i = 0; i < markup.Rows.Length; i++) {
            var row = markup.Rows[i];
            if (VisitTableRow(row, ref state) is not TableRowMarkup newRow)
                return markup;

            newRows[i] = newRow;
            isUnchanged &= ReferenceEquals(newRow, row);
        }

        return isUnchanged ? markup : new TableMarkup(newHeader, markup.Alignments, newRows);
    }

    protected override Markup VisitTableRow(TableRowMarkup markup, ref TState state)
    {
        var newCells = new TableCellMarkup[markup.Cells.Length];
        var isUnchanged = true;
        for (var i = 0; i < markup.Cells.Length; i++) {
            var cell = markup.Cells[i];
            if (VisitTableCell(cell, ref state) is not TableCellMarkup newCell)
                return markup;

            newCells[i] = newCell;
            isUnchanged &= ReferenceEquals(newCell, cell);
        }

        return isUnchanged ? markup : new TableRowMarkup(newCells);
    }

    protected override Markup VisitTableCell(TableCellMarkup markup, ref TState state)
    {
        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new TableCellMarkup(newContent);
    }

    protected override Markup VisitSeq(MarkupSeq markup, ref TState state)
    {
        var newItems = new List<Markup>();
        var isUnchanged = true;
        foreach (var item in markup.Items) {
            var newItem = Visit(item, ref state);
            if (newItem != null!)
                newItems.Add(newItem);
            isUnchanged &= newItem == item;
        }

        return isUnchanged ? markup
            : new MarkupSeq(newItems.ToArray());
    }

    protected override Markup VisitStylized(StylizedMarkup markup, ref TState state)
    {
        var newMarkup = Visit(markup.Content, ref state);
        return newMarkup == markup ? markup
            : new StylizedMarkup(newMarkup, markup.Style);
    }

    protected override Markup VisitParagraph(ParagraphMarkup markup, ref TState state)
    {
        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new ParagraphMarkup(newContent);
    }

    protected override Markup VisitHeader(HeaderMarkup markup, ref TState state)
    {
        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new HeaderMarkup(markup.Level, newContent);
    }

    protected override Markup VisitBlockQuote(BlockQuoteMarkup markup, ref TState state)
    {
        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new BlockQuoteMarkup(newContent);
    }

    protected override Markup VisitUrl(UrlMarkup markup, ref TState state) => markup;
    protected override Markup VisitMention(MentionMarkup markup, ref TState state) => markup;
    protected override Markup VisitCodeBlock(CodeBlockMarkup markup, ref TState state) => markup;

    protected override Markup VisitPlainText(PlainTextMarkup markup, ref TState state) => markup;
    protected override Markup VisitPlayableText(PlayableTextMarkup markup, ref TState state) => markup;
    protected override Markup VisitPreformattedText(PreformattedTextMarkup markup, ref TState state) => markup;
    protected override Markup VisitNewLine(NewLineMarkup markup, ref TState state) => markup;
    protected override Markup VisitUnparsed(UnparsedTextMarkup markup, ref TState state) => markup;
    protected override Markup VisitHashtag(HashtagMarkup markup, ref TState state) => markup;

    protected override Markup VisitUnknown(Markup markup, ref TState state) => markup;
}
