namespace ActualChat.Chat;

public interface IChatMentionResolverFactory : IHasServices
{
    IChatMentionResolver Create(ChatId chatId);
}
