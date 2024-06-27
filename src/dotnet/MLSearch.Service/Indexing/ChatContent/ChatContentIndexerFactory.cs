namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatContentIndexerFactory
{
    IChatContentIndexer Create();
}

internal sealed class ChatContentIndexerFactory(IServiceProvider services) : IChatContentIndexerFactory
{
    private readonly ObjectFactory<ChatContentIndexer> _factoryMethod =
        ActivatorUtilities.CreateFactory<ChatContentIndexer>([]);

    public IChatContentIndexer Create() => _factoryMethod(services, []);
}
