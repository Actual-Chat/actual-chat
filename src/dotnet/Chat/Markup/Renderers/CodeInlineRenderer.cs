using Markdig.Syntax.Inlines;

namespace ActualChat.Chat;

internal class CodeInlineRenderer : MarkupObjectRenderer<CodeInline>
{
    protected override void Write(MarkupRenderer renderer, CodeInline obj)
    {
        renderer.Parts.Add(new CodePart {
             Markup = renderer.Markup,
             Language = "",
             TextRange = new Range<int>(obj.ContentWithTrivia.Start, obj.ContentWithTrivia.End + 1),
             Code = obj.Content,
             IsInline = true
        });
    }
}
