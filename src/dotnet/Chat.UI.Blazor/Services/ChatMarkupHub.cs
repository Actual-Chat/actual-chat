namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatMarkupHub : IChatMarkupHub
{
    private IMarkupParser? _parser;
    private ChatMentionResolver? _mentionResolver;
    private ChatMentionSearchProvider? _mentionSearchProvider;
    private MentionNamer? _mentionNamer;
    private MarkupEditorHtmlConverter? _editorHtmlConverter;

    public IServiceProvider Services { get; }
    public ChatId ChatId { get; }

    public IMarkupParser Parser
        => _parser ??= Services.GetRequiredService<IMarkupParser>();

    public MentionNamer MentionNamer
        => _mentionNamer ??= new MentionNamer(MentionResolver);

    public IChatMentionResolver MentionResolver
        => _mentionResolver ??= new ChatMentionResolver(Services, ChatId);

    public ChatMentionSearchProvider MentionSearchProvider
        => _mentionSearchProvider ??= new ChatMentionSearchProvider(Services, ChatId);

    public MarkupEditorHtmlConverter EditorHtmlConverter
        => _editorHtmlConverter ??= new(this);

    public ChatMarkupHub(IServiceProvider services, ChatId chatId)
    {
        Services = services;
        ChatId = chatId;
    }
}
