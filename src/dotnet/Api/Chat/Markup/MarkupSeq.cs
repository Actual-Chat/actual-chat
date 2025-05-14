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
        using var sb = ZString.CreateStringBuilder();
        foreach (var item in Items)
            sb.Append(item.Format());
        return sb.ToString();
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
                items.Add(item);
            } else if (item is not PlainTextMarkup pt) {
                if (lastPlainText != null)
                    items.Add(lastPlainText);
                lastPlainText = null;
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
