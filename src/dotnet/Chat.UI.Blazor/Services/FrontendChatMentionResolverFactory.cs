namespace ActualChat.Chat.UI.Blazor.Services;

public class FrontendChatMentionResolverFactory : IChatMentionResolverFactory
{
    public IServiceProvider Services { get; }

    public FrontendChatMentionResolverFactory(IServiceProvider services)
        => Services = services;

    public IChatMentionResolver Create(ChatId chatId)
        => new FrontendChatMentionResolver(Services) { ChatId = chatId };
}
