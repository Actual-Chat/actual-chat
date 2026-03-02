using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents a sequence of markup elements.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class MarkupSeq : Markup
{
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
        Markup? prevItem = null;
        foreach (var item in Items) {
            // Auto-newline between blocks
            if (prevItem != null && (item.IsBlockMarkup() || prevItem.IsBlockMarkup())) {
                sb.Append(NewLineMarkup.Instance.Format());
                // Double newline between consecutive paragraphs (paragraph break)
                if (prevItem is ParagraphMarkup && item is ParagraphMarkup)
                    sb.Append(NewLineMarkup.Instance.Format());
                // Extra newline after empty paragraph to preserve empty line in output
                else if (prevItem is ParagraphMarkup p && p.Content == Markup.EmptyText)
                    sb.Append(NewLineMarkup.Instance.Format());
            }

            sb.Append(item.Format());
            prevItem = item;
        }
        return sb.ToStringAndRelease();
    }

    public override Markup Simplify()
    {
        if (Items.Length == 1)
            return Items[0].Simplify();

        var items = new List<Markup>();
        var lastPlainText = (PlainTextMarkup?)null;
        var isSimplified = false;

        foreach (var originalItem in Items) {
            var item = originalItem.Simplify();
            if (!ReferenceEquals(item, originalItem))
                isSimplified = true;

            if (item is PlainTextMarkup pt) {
                // Merge consecutive PlainText
                if (lastPlainText == null)
                    lastPlainText = pt;
                else {
                    lastPlainText = new PlainTextMarkup(lastPlainText.Text + pt.Text);
                    isSimplified = true;
                }
            }
            else {
                if (lastPlainText != null) {
                    items.Add(lastPlainText);
                    lastPlainText = null;
                }
                items.Add(item);
            }
        }

        if (lastPlainText != null)
            items.Add(lastPlainText);

        if (!isSimplified)
            return this;

        return items.Count switch {
            0 => EmptyText,
            1 => items[0],
            _ => new MarkupSeq(items.ToArray()),
        };
    }
}
