using System.Text.RegularExpressions;
using Cysharp.Text;

namespace ActualChat.Chat;

public static partial class MarkupExt
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegexFactory();
    private static readonly Regex WhitespaceRegex = WhitespaceRegexFactory();

    public static string ToReadableText(this Markup markup, MarkupConsumer consumer)
        => markup.ToReadableText(consumer.GetTrimLength());

    public static string ToReadableText(this Markup markup, int? maxLength)
    {
        var text = markup.ToReadableText();
        if (maxLength is not { } vMaxLength || text.Length <= vMaxLength)
            return text;

        text = ZString.Concat(text[..vMaxLength], "…");
        return text;
    }

    public static string ToReadableText(this Markup markup)
    {
        var text = MarkupFormatter.ReadableUnstyled.Format(markup);
        text = WhitespaceRegex.Replace(text, " ").Trim();
        return text;
    }

    public static string ToClipboardText(this Markup markup)
        => MarkupFormatter.Readable.Format(markup);
}
