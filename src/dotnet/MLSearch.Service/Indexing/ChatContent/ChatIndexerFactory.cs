namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatIndexerFactory
{
    IChatIndexer Create();
}

internal sealed class ChatIndexerFactory(IServiceProvider services) : IChatIndexerFactory
{
    private readonly ObjectFactory<ChatIndexer> _factoryMethod =
        ActivatorUtilities.CreateFactory<ChatIndexer>(Array.Empty<Type>());

    public IChatIndexer Create() => _factoryMethod(services, Array.Empty<object>());
}
