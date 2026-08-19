using System.Text;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents a sequence of markup elements.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class MarkupSeq : Markup
{
    [DataMember, Key(0)]
    public Markup[] Items { get; }

    // ReSharper disable once ConvertToPrimaryConstructor
    public MarkupSeq(params Markup[] items)
    {
        if (items.Length == 0)
            throw new ArgumentException("item list should contain at least 1 item", nameof(items));
        Items = items;
    }

    public override string Format()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        MarkupSeqFormatHelper.FormatBlockSequence(Items, sb);
        return sb.ToStringAndRelease();
    }

    public override Markup Simplify()
    {
        if (Items.Length == 1)
            return Items[0].Simplify();

        var items = new List<Markup>();
        // Consecutive PlainText merges through a StringBuilder: pairwise concatenation copies
        // the whole run once per element, and a prose paragraph is one element per word.
        var lastPlainText = (PlainTextMarkup?)null;
        var runBuilder = (StringBuilder?)null;
        var isSimplified = false;

        foreach (var originalItem in Items) {
            var item = originalItem.Simplify();
            if (!ReferenceEquals(item, originalItem))
                isSimplified = true;

            if (item is PlainTextMarkup pt) {
                if (lastPlainText == null) {
                    lastPlainText = pt;
                }
                else {
                    runBuilder ??= ActualLab.Text.StringBuilderExt.Acquire().Append(lastPlainText.Text);
                    runBuilder.Append(pt.Text);
                    isSimplified = true;
                }
            }
            else {
                FlushTextRun(items, ref lastPlainText, ref runBuilder);
                items.Add(item);
            }
        }

        FlushTextRun(items, ref lastPlainText, ref runBuilder);

        if (!isSimplified)
            return this;

        return items.Count switch {
            0 => EmptyText,
            1 => items[0],
            _ => new MarkupSeq(items.ToArray()),
        };
    }

    // Private methods

    private static void FlushTextRun(List<Markup> items, ref PlainTextMarkup? lastText, ref StringBuilder? builder)
    {
        if (builder != null) {
            items.Add(new PlainTextMarkup(builder.ToStringAndRelease()));
            builder = null;
        }
        else if (lastText != null)
            items.Add(lastText);

        lastText = null;
    }
}
