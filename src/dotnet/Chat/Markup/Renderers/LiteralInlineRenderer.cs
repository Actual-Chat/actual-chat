using System.Text.RegularExpressions;
using Markdig.Syntax.Inlines;

namespace ActualChat.Chat;

internal class LiteralInlineRenderer : MarkupObjectRenderer<LiteralInline>
{
    protected override void Write(MarkupRenderer renderer, LiteralInline obj)
    {
        var text = obj.Content.ToString();
        var startIndex = obj.Content.Start;

        var markup = renderer.Markup;
        var parts = renderer.Parts;
        var hasTextToTimeMap = !markup.TextToTimeMap.IsEmpty;

        if (hasTextToTimeMap) {
            MarkupByWordParser.ParseText(text, startIndex, parts, markup);
            return;
        }

        var emphasis = GetEmphasis(obj);
        if (emphasis != Emphasis.None) {
            parts.Add(new FormattedTextPart {
                Markup = markup,
                TextRange = (startIndex, startIndex + text.Length),
                Emphasis = emphasis
            });
            return;
        }

        parts.Add(new PlainTextPart() {
            Markup = markup,
            TextRange = (startIndex, startIndex + text.Length),
        });
    }

    private Emphasis GetEmphasis(LiteralInline literalInline)
    {
        Emphasis emphasis = Emphasis.None;
        IInline? inline = literalInline;
        while (inline!=null) {
            if (inline is EmphasisInline emphasisInline) {
                var emphasisLocal = emphasisInline.DelimiterCount == 2
                    ? Emphasis.Strong
                    : emphasisInline.DelimiterCount == 1
                        ? Emphasis.Em
                        : Emphasis.None;
                emphasis |= emphasisLocal;
            }
            inline = inline.Parent;
        }
        return emphasis;
    }
}
