using Markdig;

namespace ActualChat.Chat;

public sealed class MarkupParser
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UseAutoLinks().Build();

    public ValueTask<Markup> Parse(string text, LinearMap textToTimeMap = default)
    {
        var markup = new Markup {
            Text = text,
            TextToTimeMap = textToTimeMap,
        };

        var document = Markdown.Parse(text, _pipeline);
        var markupProto = new MarkupProto(markup);
        var renderer = new MarkupRenderer(markupProto);
        _pipeline.Setup(renderer);
        _ = renderer.Render(document);

        markup.Parts = markupProto.Parts.ToImmutableArray();
        return ValueTask.FromResult(markup);
    }
}
