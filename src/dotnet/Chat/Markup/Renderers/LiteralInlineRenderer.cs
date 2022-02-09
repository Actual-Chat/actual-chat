using System.Text.RegularExpressions;
using Markdig.Syntax.Inlines;

namespace ActualChat.Chat;

internal class LiteralInlineRenderer : MarkupObjectRenderer<LiteralInline>
{
    private static readonly Regex WordRegex = new("\\S+\\s+", RegexOptions.Compiled);

    protected override void Write(MarkupRenderer renderer, LiteralInline obj)
    {
        var text = obj.Content.ToString();

        var hasTextToTimeMap = !renderer.Markup.TextToTimeMap.IsEmpty;
        ParseText(text, renderer.Parts, hasTextToTimeMap, renderer.Markup);
        // if (renderer.EnableHtmlEscape)
        // {
        //     renderer.WriteEscape(ref obj.Content);
        // }
        // else
        // {
        //     renderer.Write(ref obj.Content);
        // }
    }

    private static void ParseText(string text, List<MarkupPart> parts, bool hasTextToTimeMap, Markup markup)
    {
        if (!hasTextToTimeMap) {
            parts.Add(new PlainTextPart() {
                Markup = markup,
                TextRange = (0, text.Length),
            });
            return;
        }

        while (true) {
            var start = parts.Count == 0 ? 0 : parts[^1].TextRange.End;
            if (start >= text.Length)
                break;

            var wordMatch = WordRegex.Match(text, start);
            if (wordMatch.Success) {
                parts.Add(new PlainTextPart()
                {
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
    }
}
