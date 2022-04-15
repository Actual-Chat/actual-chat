using Markdig.Renderers;

namespace Markdig.Extensions.Mentions;

/// <summary>
/// Extension to automatically create <see cref="MentionInline"/> when '&lt;@mention-id&gt;' is found.
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class MentionExtension : IMarkdownExtension
{
    public readonly MentionOptions Options;

    public MentionExtension(MentionOptions options)
        => Options = options ?? new MentionOptions();

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (pipeline.InlineParsers.Contains<MentionParser>()) return;
        // Insert the parser before any other parsers
        pipeline.InlineParsers.Insert(0, new MentionParser(Options));
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}
