using ActualChat.Chat;
using ActualChat.MLSearch.Indexing;

namespace ActualChat.MLSearch.Bot;

internal interface IBotConversationHandler:  ISink<ChatEntry, ChatEntryId>;

