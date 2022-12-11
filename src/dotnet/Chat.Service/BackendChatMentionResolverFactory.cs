namespace ActualChat.Chat;

public class BackendChatMentionResolverFactory : IChatMentionResolverFactory
{
    public IServiceProvider Services { get; }

    public BackendChatMentionResolverFactory(IServiceProvider services)
        => Services = services;

    public IChatMentionResolver Create(ChatId chatId)
        => new BackendChatMentionResolver(Services) { ChatId = chatId };
}
