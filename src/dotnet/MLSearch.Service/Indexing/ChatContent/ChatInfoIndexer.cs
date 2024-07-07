
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatInfoIndexer
{
    Task IndexAsync(ChatId chatId, CancellationToken cancellationToken);
}

internal sealed class ChatInfoIndexer(
    IChatsBackend chatsBackend,
    ISink<ChatInfo, string> chatInfoSink
) : IChatInfoIndexer
{
    public async Task IndexAsync(ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null) {
            throw StandardError.NotFound<Chat.Chat>($"The chat {chatId} is not found.");
        }
        var chatInfo = new ChatInfo(
            Id: chat.Id,
            IsPublic: chat.IsPublic,
            IsBotChat: chat.SystemTag.Equals(Constants.Chat.SystemTags.Bot)
        );
        await chatInfoSink.ExecuteAsync([chatInfo], [], cancellationToken).ConfigureAwait(false);
    }
}
