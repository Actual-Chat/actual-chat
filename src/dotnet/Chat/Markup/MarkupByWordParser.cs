using System.Text.RegularExpressions;

namespace ActualChat.Chat;

internal static class MarkupByWordParser
{
    private static readonly Regex WordRegex = new("\\S+\\s+", RegexOptions.Compiled);

    public static void ParseText(string text, int startIndex, List<MarkupPart> parts, Markup markup)
    {
        while (true) {
            var start = parts.Count == 0 ? 0 : parts[^1].TextRange.End;
            if (start >= text.Length)
                break;

            var wordMatch = WordRegex.Match(text, start);
            if (wordMatch.Success) {
                parts.Add(new PlainTextPart()
                {
                    Markup = markup,
                    TextRange = (startIndex + wordMatch.Index, startIndex + wordMatch.Index + wordMatch.Length),
                });
                continue;
            }

            parts.Add(new PlainTextPart() {
                Markup = markup,
                TextRange = (startIndex + start, startIndex + text.Length),
            });
        }
    }
}
