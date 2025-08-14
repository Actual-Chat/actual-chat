using System.Text.RegularExpressions;

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

        text = string.Concat(text.AsSpan(0, vMaxLength), "…");
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

    public static bool IsBlockMarkup(this Markup markup)
        => markup is CodeBlockMarkup or ListMarkup;
}
