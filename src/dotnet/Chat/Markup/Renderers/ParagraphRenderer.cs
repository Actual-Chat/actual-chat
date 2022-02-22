using Markdig.Syntax;

namespace ActualChat.Chat;

internal class ParagraphRenderer : MarkupObjectRenderer<ParagraphBlock>
{
    protected override void Write(MarkupRenderer renderer, ParagraphBlock obj)
    {
        // if (!renderer.ImplicitParagraph && renderer.EnableHtmlForBlock)
        // {
        //     if (!renderer.IsFirstInContainer)
        //     {
        //         renderer.EnsureLine();
        //     }
        //
        //     renderer.Write("<p").WriteAttributes(obj).Write(">");
        // }
        renderer.WriteLeafInline(obj);
        // if (!renderer.ImplicitParagraph)
        // {
        //     if(renderer.EnableHtmlForBlock)
        //     {
        //         renderer.WriteLine("</p>");
        //     }
        //
        //     renderer.EnsureLine();
        // }
    }
}
