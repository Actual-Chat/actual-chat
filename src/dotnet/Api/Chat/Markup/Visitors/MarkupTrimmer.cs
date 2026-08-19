
namespace ActualChat.Chat;

public interface IMarkupTrimmer
{
    Markup Trim(Markup markup, int maxLength, Func<MentionMarkup, string>? mentionFormatter = null);
}

public sealed record MarkupTrimmer : MarkupRewriter<MarkupTrimmer.State>, IMarkupTrimmer
{
    public static readonly MarkupTrimmer Instance = new();

    public Markup Trim(Markup markup, int maxLength, Func<MentionMarkup, string>? mentionFormatter = null)
    {
        mentionFormatter ??= MentionMarkup.NameOrNotAvailableFormatter;
        var state = new State(maxLength, mentionFormatter);
        return Visit(markup, ref state);
    }

    protected override Markup VisitList(ListMarkup markup, ref State state)
    {
        var newItems = new List<ListItemMarkup>();
        var isUnchanged = true;
        foreach (var item in markup.Items) {
            if (!state.CanAppend()) {
                isUnchanged = false;
                break;
            }

            var newItem = VisitListItem(item, ref state);
            if (newItem is ListItemMarkup newListItem) {
                newItems.Add(newListItem);
                isUnchanged &= newItem == item;
            }
            else
                isUnchanged = false;
        }
        if (isUnchanged)
            return markup;

        return newItems.Count > 0 ? new ListMarkup(newItems) : Markup.EmptyText;
    }

    protected override Markup VisitListItem(ListItemMarkup markup, ref State state)
    {
        if (!state.CanAppend())
            return Markup.EmptyText;

        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup
            : new ListItemMarkup(newContent);
    }

    protected override Markup VisitTable(TableMarkup markup, ref State state)
    {
        if (!state.CanAppend())
            return Markup.EmptyText;

        // Rows are dropped whole once the budget runs out: a partial row would break
        // the table's rectangular shape, which TableMarkup doesn't allow.
        if (VisitTableRow(markup.Header, ref state) is not TableRowMarkup newHeader)
            return Markup.EmptyText;

        var isUnchanged = ReferenceEquals(newHeader, markup.Header);
        var newRows = new List<TableRowMarkup>();
        foreach (var row in markup.Rows) {
            if (!state.CanAppend() || VisitTableRow(row, ref state) is not TableRowMarkup newRow) {
                isUnchanged = false;
                break;
            }

            newRows.Add(newRow);
            isUnchanged &= ReferenceEquals(newRow, row);
        }

        return isUnchanged ? markup : new TableMarkup(newHeader, markup.Alignments, newRows.ToArray());
    }

    protected override Markup VisitSeq(MarkupSeq markup, ref State state)
    {
        var newItems = new List<Markup>();
        var isUnchanged = true;
        foreach (var item in markup.Items) {
            if (!state.CanAppend()) {
                isUnchanged = false;
                break;
            }

            var newItem = Visit(item, ref state);
            if (newItem != null!)
                newItems.Add(newItem);
            isUnchanged &= newItem == item;
        }
        return isUnchanged ? markup
            : new MarkupSeq(newItems.ToArray());
    }

    protected override Markup VisitParagraph(ParagraphMarkup markup, ref State state)
    {
        if (!state.CanAppend())
            return Markup.EmptyText;

        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new ParagraphMarkup(newContent);
    }

    protected override Markup VisitHeader(HeaderMarkup markup, ref State state)
    {
        if (!state.CanAppend())
            return Markup.EmptyText;

        var newContent = Visit(markup.Content, ref state);
        return newContent == markup.Content ? markup : new HeaderMarkup(markup.Level, newContent);
    }

    // We assume any mention is of length 8
    protected override Markup VisitMention(MentionMarkup markup, ref State state)
    {
        var length = state.MentionFormatter.Invoke(markup).Length;

        if (!state.CanAppend(length))
            return state.TryAppendEllipsis();
        state.Append(length);
        return base.VisitMention(markup, ref state);
    }

    protected override Markup VisitUrl(UrlMarkup markup, ref State state)
    {
        if (!state.CanAppend(markup.Url.Length))
            return state.TryAppendEllipsis();

        state.Append(markup.Url.Length);
        return base.VisitUrl(markup, ref state);
    }

    protected override Markup VisitCodeBlock(CodeBlockMarkup markup, ref State state)
    {
        if (state.CanAppend(markup.Code.Length)) {
            state.Append(markup.Code.Length);
            return base.VisitCodeBlock(markup, ref state);
        }

        // Trim some lines
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        foreach (var (line, endsWithLineFeed) in markup.Code.ParseLines()) {
            if (!state.CanAppend(sb.Length))
                break;
            sb.Append(line);
            if (endsWithLineFeed)
                sb.Append("\r\n");
        }
        if (sb.Length == 0)
            return state.TryAppendEllipsis();

        state.Append(sb.Length);
        return new MarkupSeq(new CodeBlockMarkup(sb.ToStringAndRelease(), markup.Language), state.TryAppendEllipsis());
    }

    protected override Markup VisitText(TextMarkup markup, ref State state)
    {
        if (state.CanAppend(markup.Text.Length)) {
            state.Append(markup.Text.Length);
            return markup;
        }

        markup = markup.WithText(markup.Text.Truncate(state.MaxLength - state.Length));
        state.Append(markup.Text.Length);
        return new MarkupSeq(markup, state.TryAppendEllipsis());
    }

    // State

    public struct State(int maxLength, Func<MentionMarkup, string> mentionFormatter)
    {
        public int MaxLength { get; } = maxLength;
        public Func<MentionMarkup, string> MentionFormatter { get; } = mentionFormatter;
        public int Length { get; private set; }
        public bool IsTrimmed { get; set; }

        public Markup TryAppendEllipsis()
        {
            if (IsTrimmed)
                return PlainTextMarkup.Empty;

            if (Length < MaxLength)
                Append(MaxLength - Length);
            IsTrimmed = true;
            return new PlainTextMarkup("…");
        }

        public void Append(int count)
            => Length += count;

        public bool CanAppend() // = CanAppend(0)
            => !IsTrimmed && Length <= MaxLength;

        public bool CanAppend(int count)
            => !IsTrimmed && Length + count <= MaxLength;
    }
}
