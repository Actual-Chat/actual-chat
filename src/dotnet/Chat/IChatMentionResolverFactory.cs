namespace ActualChat.Chat;

public interface IChatMentionResolverFactory : IHasServices
{
    IChatMentionResolver Create(Symbol chatId);
}
