namespace ActualChat.Chat;

public static class MarkupHubExt
{
    public static async ValueTask<Markup> ParseAndNameMentions(
        this IChatMarkupHub chatMarkupHub,
        string markupText, CancellationToken cancellationToken)
    {
        var markup = chatMarkupHub.Parser.Parse(markupText);
        markup = await chatMarkupHub.MentionNamer.Rewrite(markup, cancellationToken).ConfigureAwait(false);
        return markup;
    }
}
