namespace ActualChat.Chat.UI.Blazor.Services;

public class MarkupHub : IHasServices
{
    private IMarkupParser? _markupParser;
    private IChatMentionResolverFactory? _chatMentionResolverFactory;
    private IChatMentionResolver? _chatMentionResolver;
    private ChatMentionSearchProvider? _chatMentionSearchProvider;
    private MentionNamer? _mentionNamer;
    private MarkupEditorHtmlConverter? _markupEditorHtmlConverter;

    public IServiceProvider Services { get; }
    public Session Session { get; }
    public Symbol ChatId { get; set; } = Symbol.Empty;

    public MarkupEditorHtmlConverter MarkupEditorHtmlConverter
        => _markupEditorHtmlConverter ??= new(this);

    public IMarkupParser MarkupParser
        => _markupParser ??= Services.GetRequiredService<IMarkupParser>();

    public MentionNamer MentionNamer {
        get {
            if (ChatId.IsEmpty)
                throw StandardError.Internal("ChatId is not set yet.");
            var mentionResolver = ChatMentionResolver;
            if (_mentionNamer == null || _mentionNamer.MentionResolver != mentionResolver)
                _mentionNamer = new(mentionResolver);
            return _mentionNamer;
        }
    }

    public IChatMentionResolver ChatMentionResolver {
        get {
            if (ChatId.IsEmpty)
                throw StandardError.Internal("ChatId is not set yet.");
            var factory = _chatMentionResolverFactory ??= Services.GetRequiredService<FrontendChatMentionResolverFactory>();
            if (_chatMentionResolver == null || _chatMentionResolver.ChatId != ChatId)
                _chatMentionResolver = factory.Create(ChatId);
            return _chatMentionResolver;
        }
    }

    public ChatMentionSearchProvider ChatMentionSearchProvider {
        get {
            if (ChatId.IsEmpty)
                throw StandardError.Internal("ChatId is not set yet.");
            if (_chatMentionSearchProvider == null || _chatMentionSearchProvider.ChatId != ChatId)
                _chatMentionSearchProvider = new(Services, ChatId);
            return _chatMentionSearchProvider;
        }
    }

    public MarkupHub(IServiceProvider services, Session session)
    {
        Services = services;
        Session = session;
    }

    public Markup Parse(string markupText)
        => MarkupParser.Parse(markupText);

    public async ValueTask<Markup> ParseAndNameMentions(string markupText, CancellationToken cancellationToken)
    {
        var markup = MarkupParser.Parse(markupText);
        markup = await MentionNamer.Rewrite(markup, cancellationToken).ConfigureAwait(false);
        return markup;
    }

    public Markup Trim(Markup markup, int maxLength)
    {
        var markupTrimmer = new MarkupTrimmer(maxLength);
        markup = markupTrimmer.Rewrite(markup);
        return markup;
    }
}
