using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatContentIndexerFactory
{
    Task<IChatContentIndexer> Create(ChatId chatId);
}

internal sealed class ChatContentIndexerFactory(IServiceProvider services) : IChatContentIndexerFactory
{
    private readonly ObjectFactory<ChatContentIndexer> _factoryMethod =
        ActivatorUtilities.CreateFactory<ChatContentIndexer>([typeof(ChatId), typeof(IChatContentArranger)]);
    private IKvas? _kvas;

    private IKvas ServerSettingsKvas => _kvas ??= services.GetRequiredService<IServerKvasBackend>().GetServerSettingsClient();

    public async Task<IChatContentIndexer> Create(ChatId chatId)
    {
        var shouldUseArranger2 = await ShouldUseArranger2(chatId).ConfigureAwait(false);
        IChatContentArranger contentArranger = shouldUseArranger2
            ? services.GetRequiredService<ChatContentArranger2>()
            : services.GetRequiredService<ChatContentArranger>();
        return _factoryMethod(services, [chatId, contentArranger]);
    }

    private async Task<bool> ShouldUseArranger2(ChatId chatId)
    {
        var chatSidList = await ServerSettingsKvas.Get<string>(Constants.ServerSettings.UseChatContentArranger2ChatIds).ConfigureAwait(false);
        if (chatSidList.IsNullOrEmpty())
            return false;

        var chatSids = chatSidList.Split(';');
        var useArranger2 = chatSids.Any(c => OrdinalEquals(chatId.Id.Value, c));
        return useArranger2;
    }
}
