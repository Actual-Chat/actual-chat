using Markdig.Syntax;

namespace ActualChat.Chat;

internal class CodeBlockRenderer : MarkupObjectRenderer<CodeBlock>
{
    protected override void Write(MarkupRenderer renderer, CodeBlock obj)
    {
        var fencedCodeBlock = obj as FencedCodeBlock;
        var language = fencedCodeBlock?.Info ?? "";

        renderer.Parts.Add(new CodePart {
            Markup = renderer.Markup,
            Language = language,
            //TextRange = new Range<int>(stringSlice.Start, stringSlice.End),
            TextRange = obj.Lines.Count>0 ? new Range<int>(obj.Lines.Lines[0].Slice.Start, obj.Lines.Lines[obj.Lines.Count - 1].Slice.End + 1) : new Range<int>(0,0),
            Code = obj.Lines.ToString()
        });
    }
}
