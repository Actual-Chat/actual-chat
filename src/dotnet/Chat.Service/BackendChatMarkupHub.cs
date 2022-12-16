namespace ActualChat.Chat;

public class BackendChatMarkupHub : IChatMarkupHub
{
    private IMarkupParser? _parser;
    private BackendChatMentionResolver? _mentionResolver;
    private MentionNamer? _mentionNamer;

    public IServiceProvider Services { get; }
    public ChatId ChatId { get; }

    public IMarkupParser Parser
        => _parser ??= Services.GetRequiredService<IMarkupParser>();

    public MentionNamer MentionNamer
        => _mentionNamer ??= new MentionNamer(MentionResolver);

    public IChatMentionResolver MentionResolver
        => _mentionResolver ??= new BackendChatMentionResolver(Services, ChatId);

    public BackendChatMarkupHub(IServiceProvider services, ChatId chatId)
    {
        Services = services;
        ChatId = chatId;
    }
}
