using Markdig.Syntax.Inlines;

namespace Markdig.Extensions.Mentions;

[DebuggerDisplay("`{MentionId}`")]
public class MentionInline : LeafInline
{
    public MentionInline(string mentionId)
    {
        MentionId = mentionId;
    }

    /// <summary>
    /// Gets or sets the mention id of the span.
    /// </summary>
    public string MentionId { get; }
}
