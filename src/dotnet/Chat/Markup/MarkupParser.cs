using System.Text.RegularExpressions;

namespace ActualChat.Chat;

public sealed class MarkupParser
{
    private static readonly Regex WordRegex = new("\\S+\\s+", RegexOptions.Compiled);

    public ValueTask<Markup> Parse(string text, LinearMap textToTimeMap = default)
    {
        var markup = new Markup() {
            Text = text,
            TextToTimeMap = textToTimeMap,
        };
        var hasTextToTimeMap = !textToTimeMap.IsEmpty;

        // NOTE(AY): Pretty dummy parsing logic - for now
        var parts = new List<MarkupPart>();
        while (true) {
            var start = parts.Count == 0 ? 0 : parts[^1].TextRange.End;
            if (start >= text.Length)
                break;

            if (!hasTextToTimeMap) {
                parts.Add(new PlainTextPart() {
                    Markup = markup,
                    TextRange = (start, text.Length),
                });
                continue;
            }

            var wordMatch = WordRegex.Match(text, start);
            if (wordMatch.Success) {
                parts.Add(new PlainTextPart() {
                    Markup = markup,
                    TextRange = (wordMatch.Index, wordMatch.Index + wordMatch.Length),
                });
                continue;
            }

            parts.Add(new PlainTextPart() {
                Markup = markup,
                TextRange = (start, text.Length),
            });
        }

        markup.Parts = parts.ToImmutableArray();
        return ValueTask.FromResult(markup);
    }
}
