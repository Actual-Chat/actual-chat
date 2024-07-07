namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatContentIndexerFactory
{
    IChatContentIndexer Create(ChatId chatId);
}

internal sealed class ChatContentIndexerFactory(IServiceProvider services) : IChatContentIndexerFactory
{
    private readonly ObjectFactory<ChatContentIndexer> _factoryMethod =
        ActivatorUtilities.CreateFactory<ChatContentIndexer>([typeof(ChatId)]);

    public IChatContentIndexer Create(ChatId chatId) => _factoryMethod(services, [chatId]);
}
