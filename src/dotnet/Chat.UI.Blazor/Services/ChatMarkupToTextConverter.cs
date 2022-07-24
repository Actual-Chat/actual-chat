namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatMarkupToTextConverter
{
    private readonly ChatUI _chatUI;
    private readonly MentionedNameResolver _mentionedNameResolver;
    private readonly IMarkupParser _markupParser;

    public ChatMarkupToTextConverter(ChatUI chatUI, MentionedNameResolver mentionedNameResolver, IMarkupParser markupParser)
    {
        _chatUI = chatUI;
        _mentionedNameResolver = mentionedNameResolver;
        _markupParser = markupParser;
    }

    public async Task<string> Convert(string markupText, CancellationToken cancellationToken = default)
    {
        var markup = _markupParser.Parse(markupText);
        var converter = new MarkupToTextConverter(_mentionedNameResolver.GetAuthorName, _mentionedNameResolver.GetUserName);
        var text = await converter.Apply(markup, cancellationToken).ConfigureAwait(false);

        return text;
    }
}
