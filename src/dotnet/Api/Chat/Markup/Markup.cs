using System.Buffers;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Base class for chat message markup elements.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[Union(0, typeof(PlainTextMarkup))]
[Union(1, typeof(PreformattedTextMarkup))]
[Union(2, typeof(UnparsedTextMarkup))]
[Union(3, typeof(NewLineMarkup))]
[Union(4, typeof(PlayableTextMarkup))]
[Union(5, typeof(AuthorMention))]
[Union(6, typeof(UserMention))]
[Union(7, typeof(ChatMention))]
[Union(8, typeof(PlaceMention))]
[Union(9, typeof(EmojiMention))]
[Union(10, typeof(ParagraphMarkup))]
[Union(11, typeof(BlockQuoteMarkup))]
[Union(12, typeof(CodeBlockMarkup))]
[Union(13, typeof(HeaderMarkup))]
[Union(14, typeof(ListMarkup))]
[Union(15, typeof(ListItemMarkup))]
[Union(16, typeof(MarkupSeq))]
[Union(17, typeof(StylizedMarkup))]
[Union(18, typeof(UrlMarkup))]
[Union(19, typeof(HashtagMarkup))]
public abstract class Markup : ISanitized
{
    protected static ArrayPool<Markup> MarkupArrayPool = ArrayPool<Markup>.Shared;

    public static Markup EmptyText => PlainTextMarkup.Empty;
    public static Markup EmptyParagraph => ParagraphMarkup.Empty;

    // Set only by a parser with AllowIncompleteMarkup: the element's closing token hasn't arrived
    // yet, so it was matched from the opening token to the end of the input. A parser without that
    // option never produces it, which is what lets rendering treat it as a streaming-only state.
    // Never serialized - it describes a transient parse of a prefix, not the message itself.
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    [IgnoreDataMember, IgnoreMember]
    public bool IsIncomplete { get; init; }

    public static Markup Join(Markup first, Markup second)
    {
        if (first == EmptyText)
            return second;
        if (second == EmptyText)
            return first;
        if (first is MarkupSeq f) {
            if (second is MarkupSeq s)
                return new MarkupSeq(f.Items.WithMany(s.Items));

            return new MarkupSeq(f.Items.With(second));
        }
        else if (second is MarkupSeq s)
            return new MarkupSeq(new[] { first }.WithMany(s.Items));

        return new MarkupSeq(first, second);
    }

    public static Markup Join(IEnumerable<Markup> parts)
    {
        var items = new List<Markup>();
        foreach (var markup in parts) {
            if (markup is MarkupSeq seq) {
                foreach (var item in seq.Items)
                    if (item != EmptyText)
                        items.Add(item);
            }
            else if (markup != EmptyText)
                items.Add(markup);
        }

        return items.Count switch {
            0 => EmptyText,
            1 => items[0],
            _ => new MarkupSeq(items.ToArray()),
        };
    }

    public override string ToString()
        => $"{GetType()}({Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(Format())})";

    public abstract string Format();

    public virtual Markup Simplify()
        => this;

    // Operators

    public static Markup operator +(Markup first, Markup second)
        => Join(first, second);
}
