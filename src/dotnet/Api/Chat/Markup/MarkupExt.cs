using System.Text.RegularExpressions;

namespace ActualChat.Chat;

/// <summary>
/// Extension methods for <see cref="Markup"/> conversion and formatting.
/// </summary>
public static partial class MarkupExt
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegexFactory();
    private static readonly Regex WhitespaceRegex = WhitespaceRegexFactory();

    public static string ToReadableText(this Markup markup, MarkupConsumer consumer)
        => markup.ToReadableText(consumer.GetTrimLength(), consumer.GetFormatter());

    public static string ToReadableText(this Markup markup, int? maxLength, IMarkupFormatter? formatter = null)
    {
        var text = markup.ToReadableText(formatter);
        if (maxLength is not { } vMaxLength || text.Length <= vMaxLength)
            return text;

        text = string.Concat(text.AsSpan(0, vMaxLength), "…");
        return text;
    }

    public static string ToReadableText(this Markup markup, IMarkupFormatter? formatter = null)
    {
        var text = (formatter ?? MarkupFormatter.ReadableUnstyled).Format(markup);
        text = WhitespaceRegex.Replace(text, " ").Trim();
        return text;
    }

    public static string ToClipboardText(this Markup markup)
        => MarkupFormatter.Readable.Format(markup);

    public static bool IsBlockMarkup(this Markup markup)
        => markup is BlockMarkup
            || (markup is MarkupSeq seq && seq.Items.Any(IsBlockMarkup));

    // True when the whole message body is a single emoji mention (ignoring paragraph/seq wrappers).
    public static bool IsSoleEmojiMention(this Markup markup)
        => markup switch {
            EmojiMention => true,
            ParagraphMarkup p => p.Content.IsSoleEmojiMention(),
            MarkupSeq s => s.Items.Length == 1 && s.Items[0].IsSoleEmojiMention(),
            _ => false,
        };

    public static bool IsPlainText(this Markup markup)
        => markup switch {
            PlainTextMarkup => true,
            NewLineMarkup => true,
            ParagraphMarkup para => para.Content.IsPlainText(),
            MarkupSeq seq => seq.Items.All(item => item.IsPlainText()),
            _ => markup == Markup.EmptyText,
        };
}
