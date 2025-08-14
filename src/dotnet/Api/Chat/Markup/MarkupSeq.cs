using ActualLab.Fusion.Blazor;
using Cysharp.Text;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class MarkupSeq : Markup
{
    public Markup[] Items { get; }

    // ReSharper disable once ConvertToPrimaryConstructor
    public MarkupSeq(params Markup[] items)
        => Items = items;

    public override string Format()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        Markup? prevItem = null;
        foreach (var item in Items) {
            // NOTE: Add new line separator between block markups and between block and inline markups.
            if (prevItem is not null && (item.IsBlockMarkup() || prevItem.IsBlockMarkup()))
                sb.Append(NewLineMarkup.Instance.Format());
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

            if (item is NewLineMarkup) {
                if (lastPlainText != null && !lastPlainText.Text.IsNullOrEmpty())
                    items.Add(lastPlainText);
                lastPlainText = null;
                var lastItem = items.LastOrDefault();
                var isBlockMarkup = lastItem?.IsBlockMarkup() ?? false;
                if (!isBlockMarkup) {
                    items.Add(item);
                    isSimplified = true;
                }
            } else if (item is not PlainTextMarkup pt) {
                if (lastPlainText != null)
                    items.Add(lastPlainText);
                lastPlainText = null;
                if (item.IsBlockMarkup()) {
                    var lastItem = items.LastOrDefault();
                    if (lastItem is NewLineMarkup)
                        items.RemoveAt(items.Count - 1);
                    isSimplified = true;
                }
                items.Add(item);
            } else if (lastPlainText == null) {
                lastPlainText = pt;
            } else {
                lastPlainText = new PlainTextMarkup(lastPlainText.Text + pt.Text);
                isSimplified = true;
            }
        }
        if (lastPlainText != null)
            items.Add(lastPlainText);

        if (!isSimplified)
            return this;
        return items.Count switch {
            0 => Empty,
            1 => items[0],
            _ => new MarkupSeq(items.ToArray()),
        };
    }
}
