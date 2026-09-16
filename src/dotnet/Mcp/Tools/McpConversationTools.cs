using System.ComponentModel;
using ActualChat.Mcp.Auth;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class McpConversationTools(IServiceProvider services)
{
    private const int DefaultLimit = 64;
    private const int MaxLimit = 1024;

    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IConversations Conversations { get; } = services.GetRequiredService<IConversations>();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "list_conversations", UseStructuredContent = true)]
    [Description("Lists summarized conversations (voice sessions) of a chat, newest first. " +
        "Pass `nextBeforeId` from a previous result as `beforeId` to go older. `limit` is capped at 1024.")]
    public async Task<McpListConversationsResult> ListConversations(
        [Description("The chat id.")] string chatId,
        [Description("Return conversations starting before this conversation id.")] string? beforeId = null,
        [Description("Max conversations to return; capped at 1024.")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var parsedChatId = ChatId.Parse(chatId);
        limit = Math.Clamp(limit, 1, MaxLimit);
        var idRange = await Chats.GetIdRange(Session, parsedChatId, cancellationToken).ConfigureAwait(false);
        if (idRange.IsEmptyOrNegative)
            return new McpListConversationsResult([], null);

        var endLid = beforeId is null ? idRange.End : ConversationId.Parse(beforeId).StartEntryLid;
        var tiles = Constants.Chat.ConversationIdTiles;
        var collected = new List<Conversation>();
        var tile = tiles.GetTile(Math.Max(endLid - 1, idRange.Start));
        while (collected.Count < limit && tile.End > idRange.Start) {
            var conversations = await Conversations.GetTile(Session, parsedChatId, tile.Range, cancellationToken)
                .ConfigureAwait(false);
            foreach (var conversation in conversations.OrderByDescending(c => c.Id.StartEntryLid)) {
                if (conversation.Id.StartEntryLid >= endLid)
                    continue;

                collected.Add(conversation);
                if (collected.Count >= limit)
                    break;
            }
            if (tile.Start <= idRange.Start)
                break;

            tile = tile.Prev();
        }
        var nextBeforeId = collected.Count >= limit ? collected[^1].Id.Value : null;
        return new McpListConversationsResult(collected.Select(c => c.ToMcpModel()).ToArray(), nextBeforeId);
    }

    [McpServerTool(Name = "get_conversation", UseStructuredContent = true)]
    [Description("Returns one conversation with its summary.")]
    public async Task<McpConversation> GetConversation(
        [Description("The conversation id (from list_conversations).")] string conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await Conversations
            .Get(Session, ConversationId.Parse(conversationId), cancellationToken)
            .Require()
            .ConfigureAwait(false);
        return conversation.ToMcpModel();
    }
}
