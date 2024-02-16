using Cysharp.Text;

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
            isUnchanged &= ReferenceEquals(newItem, item);
        }
        return isUnchanged ? markup : new MarkupSeq(newItems);
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
        using var sb = ZString.CreateStringBuilder();
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
        return new MarkupSeq(markup with { Code = sb.ToString() }, state.TryAppendEllipsis());
    }

    protected override Markup VisitText(TextMarkup markup, ref State state)
    {
        if (state.CanAppend(markup.Text.Length)) {
            state.Append(markup.Text.Length);
            return markup;
        }
        markup = markup with { Text = markup.Text.Truncate(state.MaxLength - state.Length) };
        state.Append(markup.Text.Length);
        return new MarkupSeq(markup, state.TryAppendEllipsis());
    }

    // State

    public struct State
    {
        public int MaxLength { get; }
        public Func<MentionMarkup, string> MentionFormatter { get; }
        public int Length { get; private set; }
        public bool IsTrimmed { get; set; }

        public State(int maxLength, Func<MentionMarkup, string> mentionFormatter)
        {
            MaxLength = maxLength;
            MentionFormatter = mentionFormatter;
        }

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
