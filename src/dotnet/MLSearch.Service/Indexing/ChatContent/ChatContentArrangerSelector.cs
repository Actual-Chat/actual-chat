using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatContentArrangerSelector
{
    Task<IChatContentArranger> GetContentArranger(ChatId chatId);
}

internal sealed class ChatContentArrangerSelector(IServiceProvider services) : IChatContentArrangerSelector
{
    private IKvas? _kvas;

    private IKvas ServerSettingsKvas => _kvas ??= services.GetRequiredService<IServerKvasBackend>().GetServerSettingsClient();

    public async Task<IChatContentArranger> GetContentArranger(ChatId chatId)
    {
        var useArranger2 = false;
        var chatSidList = await ServerSettingsKvas.Get<string>(Constants.ServerSettings.UseChatContentArranger2ChatIds).ConfigureAwait(false);
        if (!chatSidList.IsNullOrEmpty()) {
            var chatSids = chatSidList.Split(';');
            useArranger2 = chatSids.Any(c => OrdinalEquals(chatId.Id.Value, c));
        }

        IChatContentArranger contentArranger = useArranger2
            ? services.GetRequiredService<ChatContentArranger2>()
            : services.GetRequiredService<ChatContentArranger>();
        return contentArranger;
    }
}
