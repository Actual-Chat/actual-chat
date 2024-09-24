
namespace ActualChat.MLSearch.Bot.Tools.Context;

public interface IBotToolsContext {
    bool IsValid { get; }
    string? ConversationId { get; }
    string? UserId { get; }
}
