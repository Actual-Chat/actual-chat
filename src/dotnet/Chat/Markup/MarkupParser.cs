using Markdig;
using Markdig.Extensions.Mentions;

namespace ActualChat.Chat;

public sealed class MarkupParser
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .UseMentions(new MentionOptions { ValidateMentionId = ValidateMentionId } )
        .UsePreciseSourceLocation()
        .DisableHeadings()
        .Build();

    public ValueTask<Markup> Parse(string text, LinearMap textToTimeMap = default)
    {
        var markup = new Markup {
            Text = text,
            TextToTimeMap = textToTimeMap,
        };

        List<MarkupPart> parts;
        if (!textToTimeMap.IsEmpty) {
            parts = new ();
            MarkupByWordParser.ParseText(text, 0, parts, markup);
        }
        else {
            var document = Markdown.Parse(text, _pipeline);
            var markupProto = new MarkupProto(markup);
            var renderer = new MarkupRenderer(markupProto);
            _pipeline.Setup(renderer);
            _ = renderer.Render(document);
            parts = markupProto.Parts;
        }

        markup.Parts = parts.ToImmutableArray();
        return ValueTask.FromResult(markup);
    }

    private static bool ValidateMentionId(string mentionId)
    {
        if (ChatAuthor.IsValidId(mentionId))
            return true;
        return false;
    }
}
