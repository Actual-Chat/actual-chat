namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatContentIndexerFactory
{
    Task<IChatContentIndexer> Create(ChatId chatId);
}

internal sealed class ChatContentIndexerFactory(IServiceProvider services) : IChatContentIndexerFactory
{
    private readonly ObjectFactory<ChatContentIndexer> _factoryMethod =
        ActivatorUtilities.CreateFactory<ChatContentIndexer>([typeof(ChatId), typeof(IChatContentArranger)]);

    private IChatContentArrangerSelector ArrangerSelector { get; } = services.GetRequiredService<IChatContentArrangerSelector>();

    public async Task<IChatContentIndexer> Create(ChatId chatId)
    {
        var contentArranger = await ArrangerSelector.GetContentArranger(chatId).ConfigureAwait(false);
        return _factoryMethod(services, [chatId, contentArranger]);
    }
}
