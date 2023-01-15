using System.Text.RegularExpressions;
using Cysharp.Text;

namespace ActualChat.Chat;

public static class MarkupExt
{
    private static readonly Regex WhitespaceRe = new (@"\s+", RegexOptions.Compiled);

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
        text = WhitespaceRe.Replace(text, " ").Trim();
        return text;
    }
}
