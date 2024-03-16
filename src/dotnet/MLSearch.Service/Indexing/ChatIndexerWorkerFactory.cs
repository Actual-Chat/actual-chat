namespace ActualChat.MLSearch.Indexing;

internal interface IChatIndexerWorkerFactory
{
    IChatIndexerWorker Create(int shardIndex);
}

internal class ChatIndexerWorkerFactory(IServiceProvider services) : IChatIndexerWorkerFactory
{
    private readonly ObjectFactory<ChatIndexerWorker> factoryMethod =
        ActivatorUtilities.CreateFactory<ChatIndexerWorker>([typeof(int)]);
    public IChatIndexerWorker Create(int shardIndex) => factoryMethod(services, [shardIndex]);
}
