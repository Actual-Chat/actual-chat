using ActualChat.Chat;
using ActualChat.MLSearch.Engine.Indexing;

namespace ActualChat.MLSearch.Bot;

internal interface IBotConversationHandler:  ISink<ChatEntry, ChatEntry>;

