using System.Buffers;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Base class for chat message markup elements.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
public abstract class Markup : ISanitized
{
    protected static ArrayPool<Markup> MarkupArrayPool = ArrayPool<Markup>.Shared;

    public static Markup EmptyText => PlainTextMarkup.Empty;
    public static Markup EmptyParagraph => ParagraphMarkup.Empty;

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
            return new MarkupSeq(new [] { first }.WithMany(s.Items));
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
