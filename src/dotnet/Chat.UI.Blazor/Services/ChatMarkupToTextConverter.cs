namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatMarkupToTextConverter
{
    private readonly ChatUI _chatUI;
    private readonly MentionNameResolver _mentionNameResolver;
    private readonly IMarkupParser _markupParser;

    public ChatMarkupToTextConverter(ChatUI chatUI, MentionNameResolver mentionNameResolver, IMarkupParser markupParser)
    {
        _chatUI = chatUI;
        _mentionNameResolver = mentionNameResolver;
        _markupParser = markupParser;
    }

    public async Task<string> Convert(string markupText, CancellationToken cancellationToken = default)
    {
        var markup = _markupParser.Parse(markupText);
        var converter = new MarkupToTextConverter(_mentionNameResolver.GetAuthorName, _mentionNameResolver.GetUserName);
        var text = await converter.Apply(markup, cancellationToken).ConfigureAwait(false);

        return text;
    }
}
