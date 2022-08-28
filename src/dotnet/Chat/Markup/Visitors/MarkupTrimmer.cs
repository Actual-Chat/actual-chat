using Cysharp.Text;

namespace ActualChat.Chat;

public class MarkupTrimmer : MarkupRewriter
{
    public int MaxLength { get; }
    public MentionFormat MentionFormat { get; init; } = MentionFormat.NameOnly;

    public int Length { get; set; }
    public bool IsTrimmed { get; set; }

    public MarkupTrimmer(int maxLength)
        => MaxLength = maxLength;

    public override Markup Rewrite(Markup markup)
    {
        Length = 0;
        IsTrimmed = false;
        return base.Rewrite(markup);
    }

    protected override Markup VisitSeq(MarkupSeq markup)
    {
        var newItems = new List<Markup>();
        var isUnchanged = true;
        foreach (var item in markup.Items) {
            if (!CanAppend()) {
                isUnchanged = false;
                break;
            }

            var newItem = Visit(item);
            if (newItem != null!)
                newItems.Add(newItem);
            isUnchanged &= ReferenceEquals(newItem, item);
        }
        return isUnchanged ? markup : new MarkupSeq(newItems);
    }

    // We assume any mention is of length 8
    protected override Markup VisitMention(Mention markup)
    {
        var length = markup.Format(MentionFormat).Length;

        if (!CanAppend(length))
            return AppendEnd();
        Append(length);
        return base.VisitMention(markup);
    }

    protected override Markup VisitUrl(UrlMarkup markup)
    {
        if (!CanAppend(markup.Url.Length))
            return AppendEnd();
        Append(markup.Url.Length);
        return base.VisitUrl(markup);
    }

    protected override Markup VisitCodeBlock(CodeBlockMarkup markup)
    {
        if (CanAppend(markup.Code.Length))
            return base.VisitCodeBlock(markup);

        // Trim some lines
        using var sb = ZString.CreateStringBuilder();
        foreach (var (line, endsWithLineFeed) in markup.Code.ParseLines()) {
            if (!CanAppend(sb.Length))
                break;
            sb.Append(line);
            if (endsWithLineFeed)
                sb.Append("\r\n");
        }
        if (sb.Length == 0)
            return AppendEnd();
        Append(sb.Length);
        return new MarkupSeq(markup with { Code = sb.ToString() }, AppendEnd());
    }

    protected override Markup VisitText(TextMarkup markup)
    {
        if (!CanAppend(markup.Text.Length))
            return AppendEnd();
        markup = markup with { Text = markup.Text.Truncate(MaxLength - Length) };
        Append(markup.Text.Length);
        return new MarkupSeq(
            markup with { Text = markup.Text.Truncate(MaxLength - Length) },
            AppendEnd());
    }

    // Helpers

    protected virtual Markup AppendEnd()
    {
        if (IsTrimmed)
            return PlainTextMarkup.Empty;
        if (Length < MaxLength)
            Append(MaxLength - Length);
        IsTrimmed = true;
        return new PlainTextMarkup("…");
    }

    protected void Append(int count)
        => Length += count;

    private bool CanAppend() // = CanAppend(0)
        => !IsTrimmed && Length <= MaxLength;
    private bool CanAppend(int count)
        => !IsTrimmed && Length + count <= MaxLength;
}
