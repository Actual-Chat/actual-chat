
using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing;
internal class ChatIndexFilter(IChatsBackend chats): IChatIndexFilter {
    public async Task<bool> ChatShouldBeIndexed(ChatId chatId, CancellationToken cancellationToken){
        // Note: It should not index search chats.
        // Reason: it finds the question itself as the best match for a query
        if (!chatId.PeerChatId.IsNone) {
            // Skip 1:1 conversations
            return false;
        }
        var chat = await chats.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null) {
            // No chat info. Skip for safety.
            return false;
        }
        if (OrdinalEquals(Constants.Chat.SystemTags.Bot, chat.SystemTag)) {
            // Do not index conversations with the bot.
            return false;
        }
        if (!chat.IsPublic) {
            // Do not index private chats.
            return false;
        }

        return true;
    }
}