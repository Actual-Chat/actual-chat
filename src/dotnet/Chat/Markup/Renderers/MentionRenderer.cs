using Markdig.Extensions.Mentions;

namespace ActualChat.Chat;

internal class MentionRenderer : MarkupObjectRenderer<MentionInline>
{
    protected override void Write(MarkupRenderer renderer, MentionInline obj)
        => renderer.Parts.Add(new MentionPart {
            Markup = renderer.Markup,
            MentionId = obj.MentionId
        });
}
