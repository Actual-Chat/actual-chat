namespace ActualChat.Chat.UI.Blazor.Services;

public class MarkupHub : IHasServices
{
    private IMarkupParser? _markupParser;
    private ChatMentionResolver? _chatMentionResolver;
    private ChatMentionSearchProvider? _chatMentionSearchProvider;
    private MentionNamer? _mentionNamer;
    private MarkupEditorHtmlConverter? _markupEditorHtmlConverter;

    public IServiceProvider Services { get; }
    public ChatId ChatId { get; }

    public IMarkupParser MarkupParser
        => _markupParser ??= Services.GetRequiredService<IMarkupParser>();

    public MentionNamer MentionNamer
        => _mentionNamer ??= new MentionNamer(ChatMentionResolver);

    public ChatMentionResolver ChatMentionResolver
        => _chatMentionResolver ??= Services.ServiceFactory<ChatMentionResolver, ChatId>()[ChatId];

    public ChatMentionSearchProvider ChatMentionSearchProvider
        => _chatMentionSearchProvider ??= Services.ServiceFactory<ChatMentionSearchProvider, ChatId>()[ChatId];

    public MarkupEditorHtmlConverter MarkupEditorHtmlConverter
        => _markupEditorHtmlConverter ??= new(this);

    public MarkupHub(IServiceProvider services, ChatId chatId)
    {
        Services = services;
        ChatId = chatId;
    }

    public async ValueTask<Markup> ParseAndNameMentions(string markupText, CancellationToken cancellationToken)
    {
        var markup = MarkupParser.Parse(markupText);
        markup = await MentionNamer.Rewrite(markup, cancellationToken).ConfigureAwait(false);
        return markup;
    }

    // Private methods

    private void RequireChatId()
    {
        if (ChatId.IsNone)
            throw StandardError.Internal("ChatId is not set yet.");
    }
}
